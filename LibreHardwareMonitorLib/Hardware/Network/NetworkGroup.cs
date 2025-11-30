// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

namespace LibreHardwareMonitor.Hardware.Network;

internal class NetworkGroup : IGroup, IHardwareChanged
{
    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;

    private readonly object _updateLock = new();
    private readonly ISettings _settings;
    private readonly bool _physicalOnly;
    private List<Network> _hardware = [];

    public NetworkGroup(ISettings settings, bool physicalOnly = false)
    {
        _settings = settings;
        _physicalOnly = physicalOnly;
        UpdateNetworkInterfaces(settings);

        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAddressChanged;
    }

    public IReadOnlyList<IHardware> Hardware => _hardware;

    public string GetReport()
    {
        var report = new StringBuilder();

        foreach (Network network in _hardware)
        {
            report.AppendLine(network.NetworkInterface.Description);
            report.AppendLine(network.NetworkInterface.OperationalStatus.ToString());
            report.AppendLine();

            foreach (ISensor sensor in network.Sensors)
            {
                report.AppendLine(sensor.Name);
                report.AppendLine(sensor.Value.ToString());
                report.AppendLine();
            }
        }

        return report.ToString();
    }

    public void Close()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAddressChanged;

        foreach (Network network in _hardware)
            network.Close();
    }

    private void UpdateNetworkInterfaces(ISettings settings)
    {
        // When multiple events fire concurrently, we don't want threads interfering
        // with others as they manipulate non-thread safe state.
        lock (_updateLock)
        {
            List<NetworkInterface> networkInterfaces = GetNetworkInterfaces();
            if (networkInterfaces == null)
                return;

            List<Network> removables = [];
            List<Network> additions = [];

            List<Network> hardware = [.. _hardware];

            // Remove network interfaces that no longer exist.
            for (int i = 0; i < hardware.Count; i++)
            {
                Network network = hardware[i];
                if (networkInterfaces.Any(x => x.Id == network.NetworkInterface.Id))
                    continue;

                hardware.RemoveAt(i--);
                removables.Add(network);
            }

            // Add new ones.
            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                if (hardware.All(x => x.NetworkInterface.Id != networkInterface.Id))
                {
                    Network network = new(networkInterface, settings);
                    hardware.Add(network);
                    additions.Add(network);
                }
            }

            _hardware = hardware;

            foreach (Network removable in removables)
                HardwareRemoved?.Invoke(removable);

            foreach (Network addition in additions)
                HardwareAdded?.Invoke(addition);
        }
    }

    private List<NetworkInterface> GetNetworkInterfaces()
    {
        int retry = 0;

        while (retry++ < 5)
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                                       .Where(IsDesiredNetworkType)
                                       .OrderBy(static x => x.Name);
                
                if (_physicalOnly)
                {
                    // Filter to only physical/real adapters (exclude NDIS filters, virtual, etc.)
                    return interfaces.Where(IsPhysicalAdapter).ToList();
                }
                
                return interfaces.ToList();
            }
            catch (NetworkInformationException)
            {
                // Disabling IPv4 while running can cause a NetworkInformationException: The pipe is being closed.
                // This can be retried.
            }
        }

        return null;
    }

    private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
    {
        UpdateNetworkInterfaces(_settings);
    }

    private static bool IsDesiredNetworkType(NetworkInterface nic)
    {
        switch (nic.NetworkInterfaceType)
        {
            case NetworkInterfaceType.Loopback:
            case NetworkInterfaceType.Tunnel:
            case NetworkInterfaceType.Unknown:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Determines if a network interface is a physical adapter (not a virtual/filter adapter).
    /// Filters out NDIS lightweight filters, VirtualBox, VMware, Hyper-V, Docker, and similar.
    /// Keeps Bluetooth and WiFi adapters.
    /// </summary>
    private static bool IsPhysicalAdapter(NetworkInterface nic)
    {
        string name = nic.Name?.ToLowerInvariant() ?? "";
        string desc = nic.Description?.ToLowerInvariant() ?? "";
        
        // Exclude NDIS Lightweight Filter adapters (show as "AdapterName-FilterName-0000")
        if (name.Contains("-0000") || name.Contains("-qos packet") || 
            name.Contains("-wfp ") || name.Contains("-native wifi") ||
            name.Contains("-virtualbox") || name.Contains("-virtual wifi"))
        {
            return false;
        }
        
        // Exclude virtual/software adapters by description
        if (desc.Contains("vmware") || 
            desc.Contains("virtualbox") || desc.Contains("hyper-v") ||
            desc.Contains("docker") || desc.Contains("wsl") ||
            desc.Contains("vethernet") || desc.Contains("tap-") ||
            desc.Contains("wireguard") ||
            desc.Contains("teredo") || desc.Contains("isatap") ||
            desc.Contains("6to4") || desc.Contains("microsoft kernel debug") ||
            desc.Contains("private internet access") || // PIA VPN
            desc.Contains("nordvpn") || desc.Contains("expressvpn") ||
            desc.Contains("surfshark") || desc.Contains("protonvpn"))
        {
            return false;
        }
        
        // Exclude by name patterns for virtual/test adapters
        if (name.StartsWith("lan-verbindung*") || name.Contains("(kerneldebugger)"))
        {
            return false;
        }
        
        return true;
    }
}
