#!/usr/bin/env bash
set -euo pipefail

artifact="${1:-}"
instance_profile="${2:-}"
region="${AWS_REGION:-us-east-1}"
instance_type="${WINDOWS_TRIAGE_AWS_INSTANCE_TYPE:-t3.small}"
volume_size="${WINDOWS_TRIAGE_AWS_VOLUME_SIZE_GIB:-30}"
if [[ -z "$artifact" || -z "$instance_profile" ]]; then
  echo "Usage: $0 PATH_TO_WINDOWS_TRIAGE_EXE SSM_INSTANCE_PROFILE_NAME" >&2
  exit 2
fi
if [[ "${WINDOWS_TRIAGE_AWS_CONFIRM_COST:-}" != "yes" ]]; then
  echo "Set WINDOWS_TRIAGE_AWS_CONFIRM_COST=yes after accepting a bounded t3.medium run expected to stay below USD 5." >&2
  exit 2
fi
for tool in aws jq; do command -v "$tool" >/dev/null || { echo "Missing $tool" >&2; exit 2; }; done
[[ -f "$artifact" ]] || { echo "Missing artifact: $artifact" >&2; exit 2; }

run_id="windows-triage-$(date -u +%Y%m%d%H%M%S)-$RANDOM"
bucket="$run_id"
instance_id=""
security_group_id=""
cleanup() {
  set +e
  [[ -n "$instance_id" ]] && aws ec2 terminate-instances --region "$region" --instance-ids "$instance_id" >/dev/null
  [[ -n "$instance_id" ]] && aws ec2 wait instance-terminated --region "$region" --instance-ids "$instance_id"
  [[ -n "$security_group_id" ]] && aws ec2 delete-security-group --region "$region" --group-id "$security_group_id" >/dev/null
  aws s3 rm "s3://$bucket" --recursive --region "$region" >/dev/null 2>&1
  aws s3api delete-bucket --bucket "$bucket" --region "$region" >/dev/null 2>&1
}
trap cleanup EXIT INT TERM

account=$(aws sts get-caller-identity --query Account --output text)
bucket="$run_id-$account"
if [[ "$region" == "us-east-1" ]]; then aws s3api create-bucket --bucket "$bucket" --region "$region" >/dev/null; else aws s3api create-bucket --bucket "$bucket" --region "$region" --create-bucket-configuration "LocationConstraint=$region" >/dev/null; fi
aws s3api put-public-access-block --bucket "$bucket" --region "$region" --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
aws s3 cp "$artifact" "s3://$bucket/WindowsTriage.exe" --sse AES256 --region "$region" >/dev/null
artifact_url=$(aws s3 presign "s3://$bucket/WindowsTriage.exe" --expires-in 7200 --region "$region")

vpc_id=$(aws ec2 describe-vpcs --region "$region" --filters Name=is-default,Values=true --query 'Vpcs[0].VpcId' --output text)
security_group_id=$(aws ec2 create-security-group --region "$region" --vpc-id "$vpc_id" --group-name "$run_id" --description "No-ingress Windows Triage smoke" --query GroupId --output text)
ami=$(aws ssm get-parameter --region "$region" --name /aws/service/ami-windows-latest/Windows_Server-2025-English-Full-Base --query Parameter.Value --output text)
block_devices=$(jq -cn --argjson size "$volume_size" '[{DeviceName:"/dev/sda1",Ebs:{VolumeSize:$size,VolumeType:"gp3",Encrypted:true,DeleteOnTermination:true}}]')
instance_id=$(aws ec2 run-instances --region "$region" --image-id "$ami" --instance-type "$instance_type" --iam-instance-profile "Name=$instance_profile" --security-group-ids "$security_group_id" --metadata-options HttpTokens=required,HttpEndpoint=enabled --block-device-mappings "$block_devices" --tag-specifications "ResourceType=instance,Tags=[{Key=Name,Value=$run_id},{Key=TTL,Value=90m}]" --instance-initiated-shutdown-behavior terminate --query 'Instances[0].InstanceId' --output text)
aws ec2 wait instance-status-ok --region "$region" --instance-ids "$instance_id"

for _ in $(seq 1 60); do
  status=$(aws ssm describe-instance-information --region "$region" --filters "Key=InstanceIds,Values=$instance_id" --query 'InstanceInformationList[0].PingStatus' --output text 2>/dev/null || true)
  [[ "$status" == "Online" ]] && break
  sleep 15
done
[[ "${status:-}" == "Online" ]] || { echo "Instance did not register with SSM" >&2; exit 1; }

remote_script=$(base64 -w0 scripts/aws-windows-smoke-remote.ps1)
payload=$(mktemp)
jq -n --arg script "$remote_script" --arg url "$artifact_url" '{commands:["$script = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(\""+$script+"\"))","$path = Join-Path $env:TEMP \"aws-windows-smoke-remote.ps1\"","Set-Content -Path $path -Value $script -Encoding UTF8","& $path -ArtifactUrl \""+$url+"\""]}' > "$payload"
command_id=$(aws ssm send-command --region "$region" --instance-ids "$instance_id" --document-name AWS-RunPowerShellScript --timeout-seconds 3600 --parameters "file://$payload" --query 'Command.CommandId' --output text)
rm -f "$payload"
command_status="Pending"
for _ in $(seq 1 240); do
  command_status=$(aws ssm get-command-invocation --region "$region" --command-id "$command_id" --instance-id "$instance_id" --query Status --output text 2>/dev/null || true)
  case "$command_status" in
    Success) break ;;
    Failed|Cancelled|TimedOut|Cancelling) break ;;
  esac
  sleep 15
done
[[ "$command_status" != "Pending" && "$command_status" != "InProgress" && -n "$command_status" ]] || { echo "SSM smoke command exceeded its one-hour harness deadline" >&2; exit 1; }
result=$(aws ssm get-command-invocation --region "$region" --command-id "$command_id" --instance-id "$instance_id")
status=$(jq -r .Status <<<"$result")
[[ "$status" == "Success" ]] || { jq -r '.StandardErrorContent' <<<"$result" >&2; exit 1; }
mkdir -p .artifacts
evidence_line=$(jq -r '.StandardOutputContent' <<<"$result" | sed '/^[[:space:]]*$/d' | tail -n 1)
jq -e . <<<"$evidence_line" > ".artifacts/aws-windows-smoke-$run_id.json"
echo "AWS Windows smoke passed: .artifacts/aws-windows-smoke-$run_id.json"
