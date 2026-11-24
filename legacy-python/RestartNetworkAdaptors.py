#!/usr/bin/env python3
"""
Restart all physical network adapters (Wi-Fi, Ethernet) with automatic privilege elevation.
"""

import subprocess
import sys
import platform
import os
import time

def is_admin():
    """Check if the script has administrator/root privileges."""
    if platform.system() == "Windows":
        try:
            import ctypes
            return ctypes.windll.shell32.IsUserAnAdmin() != 0
        except:
            return False
    else:  # Linux/macOS
        return os.geteuid() == 0

def run_command(cmd, capture=True, verbose=False):
    """Run a shell command and return stdout."""
    try:
        if capture:
            result = subprocess.run(cmd, shell=True, capture_output=True, text=True, check=False)
            if verbose and result.stderr:
                print(f"[STDERR] {result.stderr.strip()}")
            return result.stdout.strip()
        else:
            subprocess.run(cmd, shell=True, check=False)
            return ""
    except Exception as e:
        print(f"Command failed: {cmd}\nError: {e}")
        return ""

def elevate():
    """Re-run the script with admin/root privileges."""
    print("Requesting administrator privileges...")
    if platform.system() == "Windows":
        # Re-launch with admin via runas
        script = os.path.abspath(sys.argv[0])
        params = ' '.join([f'"{arg}"' for arg in sys.argv[1:]])
        cmd = f'powershell -Command "Start-Process \'{sys.executable}\' -ArgumentList \'{script} {params}\' -Verb RunAs"'
        subprocess.run(cmd, shell=True)
        sys.exit(0)
    else:  # Linux/macOS
        # Re-run with sudo
        cmd = f'sudo {sys.executable} {" ".join(sys.argv)}'
        subprocess.run(cmd, shell=True)
        sys.exit(0)

def get_adapters_windows():
    """Return list of physical network adapters on Windows."""
    adapters = set()
    
    # Method 1: PowerShell (Windows 8+)
    cmd = 'powershell -Command "Get-NetAdapter -Physical | Where-Object {$_.Status -ne \'Not Present\'} | Select-Object -ExpandProperty Name"'
    out = run_command(cmd, capture=True)
    if out:
        for name in out.splitlines():
            name = name.strip()
            if name and not any(x in name.lower() for x in ['loopback', 'virtual', 'vmware', 'hyper-v']):
                adapters.add(name)
    
    # Method 2: netsh (all Windows versions)
    out = run_command('netsh interface show interface', capture=True)
    lines = out.splitlines()
    exclude = ['loopback', 'tunnel', 'virtual', 'vmware', 'virtualbox', 'hyper-v', 'wan miniport', 'ras', 'isatap', 'teredo']
    for line in lines:
        if not line.strip() or line.startswith("---") or "Admin State" in line:
            continue
        parts = line.split()
        if len(parts) >= 4:
            name = " ".join(parts[3:])
            if not any(x in name.lower() for x in exclude):
                adapters.add(name)
    
    # Method 3: wmic (old Windows fallback)
    out = run_command('wmic nic where "NetEnabled=true" get Name', capture=True)
    if out:
        for line in out.splitlines()[1:]:
            name = line.strip()
            if name and not any(x in name.lower() for x in exclude):
                adapters.add(name)
    
    return list(adapters)

def restart_adapter_windows(adapter_name):
    """Attempt to restart a network adapter using multiple methods."""
    print(f"\n>>> Restarting: {adapter_name}")
    
    # netsh disable/enable
    print("  Trying netsh...")
    run_command(f'netsh interface set interface name="{adapter_name}" admin=disable', capture=False)
    time.sleep(2)
    run_command(f'netsh interface set interface name="{adapter_name}" admin=enable', capture=False)
    time.sleep(3)
    
    # Check status
    status = run_command(f'netsh interface show interface name="{adapter_name}"', capture=True)
    if "Connected" in status or "Enabled" in status:
        print(f"  ✅ {adapter_name} restarted successfully via netsh.")
        return True
    
    # Fallback: PowerShell Restart-NetAdapter
    print("  Trying PowerShell Restart-NetAdapter...")
    ps_cmd = f'powershell -Command "Restart-NetAdapter -Name \'{adapter_name}\' -Confirm:$false"'
    out = run_command(ps_cmd, capture=True)
    if out and "error" not in out.lower():
        print(f"  ✅ {adapter_name} restarted via PowerShell.")
        return True
    
    print(f"  ❌ Failed to restart {adapter_name}. Check manually.")
    return False

def get_adapters_linux():
    """Return list of network interfaces on Linux (excluding loopback)."""
    out = run_command("ip -o link show | awk -F': ' '{print $2}'", capture=True)
    return [iface for iface in out.split('\n') if iface and iface != 'lo']

def restart_adapter_linux(adapter_name):
    """Restart Linux network interface."""
    print(f"Restarting {adapter_name}...")
    run_command(f'ip link set {adapter_name} down', capture=False)
    time.sleep(1)
    run_command(f'ip link set {adapter_name} up', capture=False)
    print(f"  ✅ {adapter_name} restarted.")

def main():
    # Auto-elevate if not admin
    if not is_admin():
        elevate()
        return  # elevate() exits current process
    
    system = platform.system()
    print(f"OS: {system}")
    
    if system == "Windows":
        adapters = get_adapters_windows()
        if not adapters:
            print("No physical network adapters found. Here are all interfaces:")
            print(run_command("netsh interface show interface", capture=True))
            return
        print(f"Found adapters: {', '.join(adapters)}")
        for adap in adapters:
            restart_adapter_windows(adap)
        print("\nAll adapters processed.")
    
    elif system == "Linux":
        adapters = get_adapters_linux()
        if not adapters:
            print("No network interfaces found.")
            return
        print(f"Found interfaces: {', '.join(adapters)}")
        for adap in adapters:
            restart_adapter_linux(adap)
    
    else:
        print(f"Unsupported OS: {system}")
        sys.exit(1)

if __name__ == "__main__":
    main()