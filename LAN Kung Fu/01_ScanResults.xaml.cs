using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace LAN_Kung_Fu
{
    /// <summary>
    /// Interaction logic for ScanResults.xaml
    /// </summary>
    public partial class ScanResults : Window
    {     
        public NetworkInterface NetworkInterface { get; set; }
        public IPAddress InterfaceIPAddress { get; set; }

        List<NetworkScan> Tabs = new List<NetworkScan>();

        public ScanResults(NetworkInterface ni)
        {
            NetworkInterface = ni;
            InterfaceIPAddress = GetAdapterIP(NetworkInterface);

            InitializeComponent();

            tab_ScanResults.Focusable = false;
        }

        public void ScanAdditional(NetworkInterface ni)
        {
            NetworkInterface = ni;
            InterfaceIPAddress = GetAdapterIP(NetworkInterface);

            //Determine if a scan has already been run on that network adapter, if so, refresh the scan
            bool exists = false;
            int index = 0;
            foreach(NetworkScan ns in Tabs)
            {
                if(ns.TabHeader.Equals(NetworkInterface.Name))
                {
                    exists = true;
                    break;
                }
                index++;
            }

            if(exists)
            {
                Tabs[index].AdapterIP = InterfaceIPAddress;
                Tabs[index].ARPResults = IPInfo.GetInterfaceIPInfo(InterfaceIPAddress);
            }
            else
            {
                Tabs.Add(new NetworkScan(NetworkInterface.Name));
                Tabs[Tabs.Count - 1].AdapterIP = InterfaceIPAddress;
                Tabs[Tabs.Count - 1].ARPResults = IPInfo.GetInterfaceIPInfo(InterfaceIPAddress);
            }            

            tab_ScanResults.ItemsSource = Tabs;
            tab_ScanResults.Items.Refresh();
        }

        private void Loaded_Scan_Results(object sender, RoutedEventArgs e)
        {
            Tabs.Add(new NetworkScan(NetworkInterface.Name));
            Tabs[0].AdapterIP = InterfaceIPAddress;
            Tabs[0].ARPResults = IPInfo.GetInterfaceIPInfo(InterfaceIPAddress);
            
            tab_ScanResults.ItemsSource = Tabs;
            tab_ScanResults.Items.Refresh();
        }

        private IPAddress GetAdapterIP(NetworkInterface ni)
        {
            IPInterfaceProperties ip_prop = ni.GetIPProperties();
            if (ip_prop == null)
            {
                return null;
            }
            else
            {
                //Get IP Address if it exists
                foreach (UnicastIPAddressInformation ip in ip_prop.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string[] octets = ip.Address.ToString().Split('.');
                        if (octets[0].Equals("169") && ip_prop.UnicastAddresses.Count <= 2)
                        {
                            continue;
                        }
                        return ip.Address;
                    }                    
                }
                return null;
            }
        }

        private void Click_btn_RefreshScan(object sender, RoutedEventArgs e)
        {
            Tabs[tab_ScanResults.SelectedIndex].ARPResults = IPInfo.GetInterfaceIPInfo(Tabs[tab_ScanResults.SelectedIndex].AdapterIP);
        }
    }

    public class NetworkScan
    {
        public string TabHeader { get; set; }
        public IPAddress AdapterIP { get; set; }
        public List<IPInfo> ARPResults { get; set; }

        public NetworkScan(string Name)
        {
            TabHeader = Name;
            ARPResults = new List<IPInfo>();
        }
    }
}
