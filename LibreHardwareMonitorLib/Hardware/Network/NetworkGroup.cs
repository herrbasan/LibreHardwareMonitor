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
    /// Uses language-independent detection methods:
    /// 1. Must have an IPv4 interface index (NDIS filters don't have one)
    /// 2. Excludes virtual adapters by checking Description (which is always in English)
    /// 3. Excludes TAP/TUN adapters (VPNs) by NetworkInterfaceType
    /// </summary>
    private static bool IsPhysicalAdapter(NetworkInterface nic)
    {
        // NDIS Lightweight Filter adapters don't have their own IPv4 interface index
        // This reliably filters out all "-QoS Packet Scheduler-", "-WFP-", etc. adapters
        try
        {
            var ipv4Props = nic.GetIPProperties()?.GetIPv4Properties();
            if (ipv4Props?.Index == null)
                return false;
        }
        catch
        {
            // If we can't get IPv4 properties, it's likely not a usable adapter
            return false;
        }

        // NetworkInterfaceType 53 is TAP/TUN adapter (used by VPNs: PIA, OpenVPN, WireGuard, etc.)
        // This is a proprietary type that .NET doesn't have an enum value for
        if ((int)nic.NetworkInterfaceType == 53)
            return false;

        // Check Description (always in English, even on non-English Windows)
        string desc = nic.Description?.ToLowerInvariant() ?? "";
        
        // Exclude virtual adapters by description keywords
        if (desc.Contains("virtual") ||     // Microsoft Wi-Fi Direct Virtual Adapter, VMware, Hyper-V
            desc.Contains("virtualbox") ||  // VirtualBox Host-Only Ethernet Adapter
            desc.Contains("vmware") ||      // VMware Network Adapter
            desc.Contains("hyper-v") ||     // Hyper-V Virtual Ethernet Adapter
            desc.Contains("docker") ||      // Docker virtual network
            desc.Contains("wsl"))           // Windows Subsystem for Linux
        {
            return false;
        }
        
        return true;
    }
}
