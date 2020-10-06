using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace LAN_Kung_Fu
{
    public class NetworkManagement : IDisposable
    {
        /// <summary>
        /// Set's a new IP Address and it's Subnet Mask of the local machine
        /// </summary>
        /// <param name="ip_address">The IP Address</param>
        /// <param name="subnet_mask">The Submask IP Address</param>
        /// <remarks>Requires a reference to the System.Management namespace</remarks>
        public void setIP(string ip_address, string subnet_mask, string adapter_desc, bool ip_auto)
        {
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                string description = objMO["Description"] as string;

                if (adapter_desc.Contains(description))
                {
                    { 
                        try
                        {
                            if (!ip_auto)
                            {
                                ManagementBaseObject setIP;
                                ManagementBaseObject newIP = objMO.GetMethodParameters("EnableStatic");

                                string[] ips = objMO["IPAddress"] as string[];

                                newIP["IPAddress"] = new string[] { ip_address };
                                newIP["SubnetMask"] = new string[] { subnet_mask };

                                setIP = objMO.InvokeMethod("EnableStatic", newIP, null);
                            }
                            else
                            {
                                if ((bool)objMO["IPEnabled"])
                                {
                                    object result = objMO.InvokeMethod("EnableDHCP", new object[] { });
                                }
                                else
                                {
                                    ManagementClass mc = new ManagementClass("Win32_NetworkAdapter");
                                    ManagementObjectCollection moc = mc.GetInstances();

                                    foreach(ManagementObject mo in moc)
                                    {
                                        if (mo["Description"].Equals(description))
                                        {
                                            string guid = mo["GUID"] as string;
                                            string k = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid;
                                            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                                            {
                                                using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid, true))
                                                {
                                                    if (key != null)
                                                    {
                                                        key.SetValue("EnableDHCP", "1", RegistryValueKind.String);
                                                        key.Close();
                                                    }
                                                }
                                            }
                                            object result = objMO.InvokeMethod("RenewDHCPLease", new object[] { });
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Set's a new Gateway address of the local machine
        /// </summary>
        /// <param name="gateway">The Gateway IP Address</param>
        /// <remarks>Requires a reference to the System.Management namespace</remarks>
        public void setGateway(string gateway, string adapter_desc)
        {
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                string description = objMO["Description"] as string;

                if (adapter_desc.Contains(description))
                {
                    if (!String.IsNullOrEmpty(gateway))
                    {
                        try
                        {
                            ManagementBaseObject setGateway;
                            ManagementBaseObject newGateway = objMO.GetMethodParameters("SetGateways");

                            newGateway["DefaultIPGateway"] = new string[] { gateway };
                            newGateway["GatewayCostMetric"] = new int[] { 1 };

                            setGateway = objMO.InvokeMethod("SetGateways", newGateway, null);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                    else
                    {
                        ManagementClass mc = new ManagementClass("Win32_NetworkAdapter");
                        ManagementObjectCollection moc = mc.GetInstances();

                        foreach (ManagementObject mo in moc)
                        {
                            if (mo["Description"].Equals(description))
                            {
                                string guid = mo["GUID"] as string;
                                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                                {
                                    using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid, true))
                                    {
                                        if (key != null)
                                        {
                                            key.SetValue("DefaultGateway", "", RegistryValueKind.String);
                                            key.Close();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Set's the DNS Server of the local machine
        /// </summary>
        /// <param name="NIC">NIC address</param>
        /// <param name="DNS">DNS server address</param>
        /// <remarks>Requires a reference to the System.Management namespace</remarks>
        public void setDNS(string DNS, string adapter_desc)
        {
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                string description = objMO["Description"] as string;

                if (adapter_desc.Contains(description))
                {
                    try
                    {
                        ManagementBaseObject newDNS = objMO.GetMethodParameters("SetDNSServerSearchOrder");
                        if(!String.IsNullOrEmpty(DNS))
                        {
                            newDNS["DNSServerSearchOrder"] = DNS.Split(',');
                        }
                        else
                        {
                            newDNS["DNSServerSearchOrder"] = null;
                        }
                        
                        ManagementBaseObject setDNS =
                            objMO.InvokeMethod("SetDNSServerSearchOrder", newDNS, null);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
        }
        /// <summary>
        /// Set's WINS of the local machine
        /// </summary>
        /// <param name="NIC">NIC Address</param>
        /// <param name="priWINS">Primary WINS server address</param>
        /// <param name="secWINS">Secondary WINS server address</param>
        /// <remarks>Requires a reference to the System.Management namespace</remarks>
        public void setWINS(string NIC, string priWINS, string secWINS)
        {
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                if ((bool)objMO["IPEnabled"])
                {
                    if (objMO["Caption"].Equals(NIC))
                    {
                        try
                        {
                            ManagementBaseObject setWINS;
                            ManagementBaseObject wins =
                            objMO.GetMethodParameters("SetWINSServer");
                            wins.SetPropertyValue("WINSPrimaryServer", priWINS);
                            wins.SetPropertyValue("WINSSecondaryServer", secWINS);

                            setWINS = objMO.InvokeMethod("SetWINSServer", wins, null);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        public bool IsDHCPEnabled(string adapter_desc)
        {
            ManagementClass mc = new ManagementClass("Win32_NetworkAdapter");
            ManagementObjectCollection moc = mc.GetInstances();

            bool value = false;
            foreach (ManagementObject objMO in moc)
            {
                string description = objMO["Description"] as string;
                if (adapter_desc.Contains(description))
                {
                    string guid = objMO["GUID"] as string;
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid, true))
                        {
                            if (key != null)
                            {
                                string kv = key.GetValue("EnableDHCP").ToString();
                                key.Close();
                                if(kv.Equals("1"))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return value;
        }

        public bool GetNetEnabled(string adapter_desc)
        {
            string strWQuery = "SELECT DeviceID, ProductName, Description, "
                                + "NetEnabled, NetConnectionStatus "
                                + "FROM Win32_NetworkAdapter "
                                + "WHERE Manufacturer <> 'Microsoft' ";
            ObjectQuery oQuery = new System.Management.ObjectQuery(strWQuery);
            ManagementObjectSearcher oSearcher = new ManagementObjectSearcher(oQuery);
            ManagementObjectCollection oReturnCollection = oSearcher.Get();

            bool value = false;
            foreach (ManagementObject mo in oReturnCollection)
            {
                string description = mo["Description"] as string;
                
                if (adapter_desc.Contains(description))
                {
                    try
                    {
                        value = Convert.ToBoolean(mo["NetEnabled"]);
                        if(value)
                        {
                            return value;
                        }
                    }   
                    catch
                    {

                    }                     
                }                
            }
            return value;
        }

        public void SetDisabled(string adapter_desc)
        {
            string strWQuery = "SELECT DeviceID, ProductName, Description, "
                                + "NetEnabled, NetConnectionStatus "
                                + "FROM Win32_NetworkAdapter "
                                + "WHERE Manufacturer <> 'Microsoft' ";
            ObjectQuery oQuery = new System.Management.ObjectQuery(strWQuery);
            ManagementObjectSearcher oSearcher = new ManagementObjectSearcher(oQuery);
            ManagementObjectCollection oReturnCollection = oSearcher.Get();

            foreach (ManagementObject mo in oReturnCollection)
            {
                string description = mo["Description"] as string;

                if (adapter_desc.Contains(description))
                {
                    try
                    {
                        mo.InvokeMethod("Disable", null);

                    }
                    catch
                    {

                    }
                }
            }
        }

        public void SetEnabled(string adapter_desc)
        {
            string strWQuery = "SELECT DeviceID, ProductName, Description, "
                                + "NetEnabled, NetConnectionStatus "
                                + "FROM Win32_NetworkAdapter "
                                + "WHERE Manufacturer <> 'Microsoft' ";
            ObjectQuery oQuery = new System.Management.ObjectQuery(strWQuery);
            ManagementObjectSearcher oSearcher = new ManagementObjectSearcher(oQuery);
            ManagementObjectCollection oReturnCollection = oSearcher.Get();

            foreach (ManagementObject mo in oReturnCollection)
            {
                string description = mo["Description"] as string;

                if (adapter_desc.Contains(description))
                {
                    try
                    {
                        mo.InvokeMethod("Enable", null);

                    }
                    catch
                    {

                    }
                }
            }
        }

        public void Dispose()
        {

        }
    }
}
