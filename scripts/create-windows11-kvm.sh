#!/usr/bin/env bash
set -euo pipefail

iso="${1:-}"
vm_dir="${2:-$PWD/.artifacts/windows11-vm}"
name="windows-triage-win11"
[[ -f "$iso" ]] || { echo "Usage: $0 PATH_TO_OFFICIAL_WINDOWS11_ENTERPRISE_ISO [VM_DIR]" >&2; exit 2; }
for tool in qemu-img virt-install virsh swtpm; do command -v "$tool" >/dev/null || { echo "Missing $tool" >&2; exit 2; }; done
[[ -r /dev/kvm && -w /dev/kvm ]] || { echo "/dev/kvm is unavailable to this user" >&2; exit 2; }
available_kib=$(df --output=avail "$vm_dir" 2>/dev/null | tail -n 1 || df --output=avail "$(dirname "$vm_dir")" | tail -n 1)
(( available_kib >= 55 * 1024 * 1024 )) || { echo "At least 55 GiB free is required" >&2; exit 2; }
if virsh dominfo "$name" >/dev/null 2>&1; then echo "VM $name already exists; refusing to overwrite it" >&2; exit 2; fi
mkdir -p "$vm_dir"
disk="$vm_dir/$name.qcow2"
qemu-img create -f qcow2 "$disk" 48G
virt-install --name "$name" --memory 8192 --vcpus 4 --cpu host-passthrough --os-variant win11 \
  --disk "path=$disk,bus=virtio,format=qcow2" --cdrom "$iso" --boot uefi \
  --tpm backend.type=emulator,backend.version=2.0 --network network=default,model=virtio \
  --graphics spice --video virtio --noautoconsole
echo "VM created. Complete installation interactively, snapshot the clean install, then follow docs/windows-11-kvm-smoke.md."
