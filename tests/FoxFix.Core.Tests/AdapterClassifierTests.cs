using System.Net.NetworkInformation;
using FluentAssertions;
using FoxFix.Core.Network;
using Xunit;

namespace FoxFix.Core.Tests;

public class AdapterClassifierTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, "Intel(R) Wi-Fi 6 AX201 160MHz")]
    [InlineData(NetworkInterfaceType.Ethernet, "Realtek PCIe GbE Family Controller")]
    [InlineData(NetworkInterfaceType.GigabitEthernet, "Intel(R) Ethernet Connection")]
    public void Real_adapters_are_physical(NetworkInterfaceType type, string description)
    {
        AdapterClassifier.IsPhysical(type, description).Should().BeTrue();
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, "Hyper-V Virtual Ethernet Adapter")]
    [InlineData(NetworkInterfaceType.Ethernet, "VMware Virtual Ethernet Adapter for VMnet8")]
    [InlineData(NetworkInterfaceType.Ethernet, "TAP-Windows Adapter V9")]
    [InlineData(NetworkInterfaceType.Wireless80211, "Microsoft Wi-Fi Direct Virtual Adapter #2")]
    [InlineData(NetworkInterfaceType.Ethernet, "WireGuard Tunnel")]
    [InlineData(NetworkInterfaceType.Ethernet, "Bluetooth Device (Personal Area Network)")]
    [InlineData(NetworkInterfaceType.Loopback, "Software Loopback Interface 1")]
    [InlineData(NetworkInterfaceType.Tunnel, "Teredo Tunneling Pseudo-Interface")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3-WFP Native MAC Layer LightWeight Filter-0000")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3-WinpkFilter NDIS LightWeight Filter-0000")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3-QoS Packet Scheduler-0000")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet (Kernel Debugger)")]
    [InlineData(NetworkInterfaceType.Ethernet, "Microsoft Kernel Debug Network Adapter")]
    public void Virtual_and_non_ethernet_adapters_are_not_physical(NetworkInterfaceType type, string description)
    {
        AdapterClassifier.IsPhysical(type, description).Should().BeFalse();
    }
}
