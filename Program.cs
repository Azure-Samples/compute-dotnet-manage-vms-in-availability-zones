// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information. 

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Compute.Models;

namespace ManageZonalVirtualMachine
{
    public class Program
    {
        /**
         * Azure Compute sample for managing virtual machines -
         *  - Create a zonal virtual machine with implicitly zoned related resources (PublicIP, Disk)
         *  - Creates a zonal PublicIP address
         *  - Creates a zonal managed data disk
         *  - Create a zonal virtual machine and associate explicitly created zonal PublicIP and data disk.
         */
        private static ResourceIdentifier? _resourceGroupId = null;
        public static async Task RunSample(ArmClient client)
        {
            var region = AzureLocation.EastUS2;
            var rgName = Utilities.CreateRandomName("rgCOMV");
            var vmName1 = Utilities.CreateRandomName("lVM1");
            var vmName2 = Utilities.CreateRandomName("lVM2");
            var pipName1 = Utilities.CreateRandomName("pip1");
            var pipName2 = Utilities.CreateRandomName("pip2");
            var diskName = Utilities.CreateRandomName("ds");
            var userName = Utilities.CreateUsername();
            var password = Utilities.CreatePassword();
            try
            {
                //=============================================================

                // Create a Linux VM in an availability zone
               
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();
                Utilities.Log($"Creating a resource group with name : {rgName}...");
                var rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(region));
                var resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                // Create a linux virtual network
                Utilities.Log("Creating a Linux virtual network...");
                var virtualNetworkName = Utilities.CreateRandomName("VirtualNetwork_");
                var virtualNetworkCollection = resourceGroup.GetVirtualNetworks();
                var data = new VirtualNetworkData()
                {
                    Location = region,
                    AddressPrefixes =
                    {
                        new string("10.0.0.0/28"),
                    },
                };
                var virtualNetworkLro = await virtualNetworkCollection.CreateOrUpdateAsync(WaitUntil.Completed, virtualNetworkName, data);
                var virtualNetwork = virtualNetworkLro.Value;
                Utilities.Log("Created a Linux virtual network with name : " + virtualNetwork.Data.Name);

                // Create a public IP address
                Utilities.Log("Creating a Linux Public IP address...");
                var publicAddressIPCollection = resourceGroup.GetPublicIPAddresses();
                var publicIPAddressdata = new PublicIPAddressData()
                {
                    Location = region,
                    Sku = new PublicIPAddressSku()
                    {
                        Name = PublicIPAddressSkuName.Standard,
                    },
                    PublicIPAddressVersion = NetworkIPVersion.IPv4,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Static,
                };
                var publicIPAddressLro = await publicAddressIPCollection.CreateOrUpdateAsync(WaitUntil.Completed, pipName1, publicIPAddressdata);
                var publicIPAddress = publicIPAddressLro.Value;
                Utilities.Log("Creatied a Linux Public IP address with name : " + publicIPAddress.Data.Name);

                //Create a subnet
                Utilities.Log("Creating a Linux subnet...");
                var subnetName = Utilities.CreateRandomName("subnet_");
                var subnetData = new SubnetData()
                {
                    ServiceEndpoints =
                    {
                        new ServiceEndpointProperties()
                        {
                            Service = "Microsoft.Storage"
                        }
                    },
                    Name = subnetName,
                    AddressPrefix = "10.0.0.0/28",
                };
                var subnetLRro = await virtualNetwork.GetSubnets().CreateOrUpdateAsync(WaitUntil.Completed, subnetName, subnetData);
                var subnet = subnetLRro.Value;
                Utilities.Log("Created a Linux subnet with name : " + subnet.Data.Name);

                //Create a networkInterface
                Utilities.Log("Created a linux networkInterface");
                var networkInterfaceData = new NetworkInterfaceData()
                {
                    Location = region,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{virtualNetwork.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = publicIPAddress.Data,
                        }
                    }
                };
                var networkInterfaceName = Utilities.CreateRandomName("networkInterface");
                var nic = (await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName, networkInterfaceData)).Value;
                Utilities.Log("Created a Linux networkInterface with name : " + nic.Data.Name);

                //Create a VM with the Public IP address
                Utilities.Log("Creating a zonal VM with implicitly zoned related resources (PublicIP, Disk)");
                var virtualMachineCollection = resourceGroup.GetVirtualMachines();
                var linuxComputerName = Utilities.CreateRandomName("linuxComputer");
                var linuxVmdata = new VirtualMachineData(region)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = userName,
                        AdminPassword = password,
                        ComputerName = linuxComputerName,
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                        {
                            OSType = SupportedOperatingSystemType.Linux,
                            Caching = CachingType.ReadWrite,
                            ManagedDisk = new VirtualMachineManagedDisk()
                            {
                                StorageAccountType = StorageAccountType.StandardLrs
                            }
                        },
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                        },
                    },
                    Zones =
                    {
                        "1"
                    },
                };
                var virtualMachine1Lro = await virtualMachineCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmName1, linuxVmdata);
                var virtualMachine1 = virtualMachine1Lro.Value;
                Utilities.Log("Created a zonal virtual machine: " + virtualMachine1.Id);

                //=============================================================

                // Create a zonal PublicIP address
                Utilities.Log("Creating a zonal public ip address...");
                var zonalPipCollection = resourceGroup.GetPublicIPAddresses();
                var zonalPipData = new PublicIPAddressData()
                {
                    Location = region,
                    Sku = new PublicIPAddressSku()
                    {
                        Name = PublicIPAddressSkuName.Standard,
                    },
                    PublicIPAddressVersion = NetworkIPVersion.IPv4,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Static,
                    Zones =
                    {
                        "1"
                    }
                };
                var zonalPipLro = await zonalPipCollection.CreateOrUpdateAsync(WaitUntil.Completed, pipName2, zonalPipData);
                var zonalPip = zonalPipLro.Value;
                Utilities.Log("Created a zonal public ip address: " + zonalPip.Data.Name);

                //=============================================================

                // Create a zonal managed data disk
                Utilities.Log("Creating a zonal data disk...");
                var dataDiskCollection = resourceGroup.GetManagedDisks();
                var dataDiskData = new ManagedDiskData(region)
                {
                    Sku = new DiskSku()
                    {
                        Name = DiskStorageAccountType.StandardSsdLrs,
                    },
                    DiskSizeGB = 100,
                    Zones = { "1" },
                    CreationData = new DiskCreationData(DiskCreateOption.Empty)
                };
                var dataDisk = (await dataDiskCollection.CreateOrUpdateAsync(WaitUntil.Completed, diskName, dataDiskData)).Value;
                Utilities.Log("Created a zoned managed data disk: " + dataDisk.Data.Id);

                //=============================================================

                // Create a zonal virtual machine with zonal public ip and data disk

                // Create a networkInterface
                Utilities.Log("Creating a zonal networkInterface...");
                var zonalNetworkInterfaceData = new NetworkInterfaceData()
                {
                    Location = region,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{virtualNetwork.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = zonalPip.Data,
                        }
                    }
                };
                var zonalNetworkInterfaceName = Utilities.CreateRandomName("networkInterface");
                var zonalNic = (await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, zonalNetworkInterfaceName, zonalNetworkInterfaceData)).Value;
                Utilities.Log("Created zonal network interface with name : " + zonalNic.Data.Name);

                //Create a VM with the Public IP address
                Utilities.Log("Creating a zonal VM with implicitly zoned related resources (PublicIP, Disk)");
                var zonalComputerName = Utilities.CreateRandomName("zonalComputer");
                var zonalVmdata = new VirtualMachineData(region)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = userName,
                        AdminPassword = password,
                        ComputerName = zonalComputerName,
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = zonalNic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                        {
                            OSType = SupportedOperatingSystemType.Linux,
                            Caching = CachingType.ReadWrite,
                            ManagedDisk = new VirtualMachineManagedDisk()
                            {
                                StorageAccountType = StorageAccountType.StandardLrs
                            }
                        },
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                        },
                        DataDisks =
                        {
                           new VirtualMachineDataDisk(0, DiskCreateOptionType.Attach)
                           {
                               ManagedDisk = new VirtualMachineManagedDisk()
                               {
                                   Id = dataDisk.Id
                               }
                           }
                        }
                    },
                    Zones =
                    {
                        "1"
                    },
                };
                var virtualMachine2Lro = await virtualMachineCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmName2, zonalVmdata);
                var virtualMachine2 = virtualMachine2Lro.Value;
                Utilities.Log("Created a zoned virtual machine: " + virtualMachine2.Id);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group: {_resourceGroupId}");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }
        public static async Task Main(string[] args)
        {
            try
            {
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);
                await RunSample(client);
            }
            catch (Exception e)
            {
                Utilities.Log(e);
            }
        }
    }
}
