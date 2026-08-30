# AWS Windows Server smoke test

AWS smoke is supplemental Windows runtime evidence. The authoritative GUI/UAC/SmartScreen gate remains the Windows 11 KVM test because AWS-managed EC2 images are Windows Server.

## What it verifies

- Windows Server 2025 boot and SSM management with no inbound ports.
- True waited execution of WinExe `--help`, `--version`, and elevated Quick collection.
- Required report and privacy-manifest generation.
- Default privacy sentinels against machine name, user-profile paths, WMI system name, serials, processor IDs, and PNP IDs.
- JSON evidence capture and complete EC2, IAM, security-group, and S3 cleanup.

## Prerequisites

- AWS CLI v2 and `jq`.
- Temporary AWS credentials from IAM Identity Center or an assumed role; do not use root credentials for routine automation.
- A default VPC in the selected region.
- An EC2 instance profile containing `AmazonSSMManagedInstanceCore`.
- A current self-contained `win-x64` executable.

## Run

```bash
dotnet publish src/WindowsTriage.App/WindowsTriage.App.csproj \
  -c Release -r win-x64 --self-contained true --no-restore

WINDOWS_TRIAGE_AWS_CONFIRM_COST=yes \
AWS_REGION=us-east-1 \
./scripts/aws-windows-smoke.sh \
  src/WindowsTriage.App/bin/Release/net10.0-windows10.0.22000.0/win-x64/publish/WindowsTriage.exe \
  YOUR_SSM_INSTANCE_PROFILE
```

Defaults are `t3.small` and a 30-GiB encrypted `gp3` root volume. Override them only when needed:

```bash
WINDOWS_TRIAGE_AWS_INSTANCE_TYPE=t3.medium \
WINDOWS_TRIAGE_AWS_VOLUME_SIZE_GIB=60 \
WINDOWS_TRIAGE_AWS_CONFIRM_COST=yes \
./scripts/aws-windows-smoke.sh PATH_TO_EXE PROFILE_NAME
```

The script refuses to run without explicit cost confirmation. It uses the current Windows Server 2025 AMI from AWS Systems Manager Parameter Store, IMDSv2, no-ingress networking, a 90-minute TTL tag, encrypted temporary S3 transfer, bounded SSM polling, and cleanup traps.

## Evidence and cleanup

Successful evidence is written under ignored `.artifacts/` as JSON. A pass is valid only when the file contains parseable JSON and reports `result: pass`.

After every run, confirm no resources with the `windows-triage-` prefix remain among instance profiles, roles, active instances, non-default security groups, or S3 buckets. The harness performs this cleanup automatically, but release evidence should record an independent confirmation.

The verified `v0.3.0-beta.1` development run passed on Windows Server 2025 Datacenter build `26100`, using a Free Tier-eligible `t3.small`. It generated all required reports and passed the default privacy assertions. This does not replace Windows 11 client validation or SignPath signing.
