using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NativeWifi;
using System.Collections.ObjectModel;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using System.Deployment.Application;
using Microsoft.Win32;
using System.Reflection;
using System.Diagnostics;

namespace LAN_Kung_Fu
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        TreeViewItem selectedItem;              //currently selected "focused" item in the treeview

        Profile focusedProfile;                 //currently focused profile in the treeview

        NetworkInterface sel_ni;                //currently selected network interface in the combobox
        NetworkInterface[] bgnd_ni;             //array of network adapters refreshed in a non-UI threaded task 

        IPAddress AdapterIPAddress;             //current IP address of selected network adapter, used for network scan

        ScanResults sr;

        bool selectedProject;                   //is a project selected in the treeview?

        //Timeout label definition
        Progress<string> timeout_btn_Handler;
        IProgress<string> timeout_btn;
        //IP Information label definitions
        Progress<string[]> IP_Labels_Handler;
        IProgress<string[]> IP_Labels;
        //Update Check definition
        Progress<bool> Update_Handler;
        IProgress<bool> Update;

        int timeout = 30;                       //seconds until apply settings timeout
        int timeElapsed = 0;                    //elapsed time since apply button was clicked
        int newItem_Num;                        //int used to determine the default number at the end of a new project or profile name

        //Used for persistent information across instances
        string AppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string roamingFolder = @"\LAN Kung Fu\";
        string updatePath = @"not_implemented";

        public MainWindow()
        {
            //try
            //{
            //    //IP Information Label Definition
            //    Update_Handler = new Progress<bool>(value =>
            //    {
            //        StartUpdate();
            //    });
            //    Update = Update_Handler as IProgress<bool>;
            //    //Check for updates asynchronously
            //    Task.Run(() =>
            //    {
            //        CheckForUpdates();
            //    });
            //}
            //catch
            //{
            //    //Unable to get product version, continue with program execution
            //}

            InitializeComponent();
            CreateDirectories();
            PopulateNetworkAdapters();
            ReadPersistentData();

            //Definition of apply button to show time until timeout
            timeout_btn_Handler = new Progress<string>(value =>
            {
                btn_Apply.Content = value;
            });
            timeout_btn = timeout_btn_Handler as IProgress<string>;                      
            
            //Start periodic task to query current network adapter settings at the defined duetime and interval
            RunPeriodicAsync(OnTick, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1000), CancellationToken.None);

            //Scan network button disabled and image gray until network adapter is selected
            img_btn_ScanNetwork.Opacity = .40;
            btn_ScanNetwork.IsEnabled = false;
            
            //Set window content relavent to applying network settings to "invisible"
            lbl_Progress.Content = "";
            bar_Progress.Visibility = Visibility.Hidden;
            grid_DesiredSettings.Visibility = Visibility.Hidden;
        }

        #region WindowEvents
        //Save application data to appdata folder when the window is closed normally by user
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            WritePersistentData();
        }
        #endregion

        #region TreeViewEvents
        //Focus the right-clicked treeview item on user right-click
        private void MouseRightButtonDown_tv_Projects_Item(object sender, MouseEventArgs e)
        {
            selectedItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);

            if (selectedItem != null)
            {
                selectedItem.Focus();
                e.Handled = true;
            }
        }

        //Used to determine right-clicked treeview item
        static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = VisualTreeHelper.GetParent(source);

            return source as TreeViewItem;
        }

        //Defined so that a double clicked treeview item does not collapse the item but only edits
        private void MouseDoubleClick_tv_Projects_Item(object sender, MouseEventArgs e)
        {
            e.Handled = true;
        }

        //Maintains the currently selected treeview item and defines the correct network information if a profile
        private void SelectedItemChanged_tv_Projects(object sender, RoutedEventArgs e)
        {
            if (tv_Projects.SelectedItem != null)
            {                
                focusedProfile = (Profile)tv_Projects.SelectedItem;
                if (!focusedProfile.Project)
                {
                    grid_DesiredSettings.Visibility = Visibility.Visible;
                    btn_Apply.IsEnabled = true;
                }
                else
                {
                    grid_DesiredSettings.Visibility = Visibility.Hidden;
                }

                //Selects the correct network adapter in the combobox using the network name and an index
                int i = 0;
                foreach (NetworkInterface ni in cb_NetworkAdapters.Items)
                {
                    if (ni.Name.Equals(focusedProfile.NetworkName))
                    {
                        cb_NetworkAdapters.SelectedIndex = i;
                    }
                    i++;
                }

                //Sets the IP settings to automatic or manual
                if (focusedProfile.IPAuto)
                {
                    rb_DesiredIP_IPAuto.IsChecked = true;
                }
                else
                {
                    rb_DesiredIP_IPManual.IsChecked = true;
                }

                //Sets the DNS settings to automatic or manual
                if (focusedProfile.DNSAuto)
                {
                    rb_DesiredIP_DNSAuto.IsChecked = true;
                }
                else
                {
                    rb_DesiredIP_DNSManual.IsChecked = true;
                }

                //Sets the IP Address
                if(focusedProfile.IPAddress != null)
                {
                    string[] IP_Octets = focusedProfile.IPAddress.ToString().Split('.');
                    if (IP_Octets.Length == 4)
                    {
                        tb_DesiredIP_FirstOct.Text = IP_Octets[0];
                        tb_DesiredIP_SecondOct.Text = IP_Octets[1];
                        tb_DesiredIP_ThirdOct.Text = IP_Octets[2];
                        tb_DesiredIP_FourthOct.Text = IP_Octets[3];
                    }
                    else
                    {
                        focusedProfile.IPAddress = null;
                    }
                }  
                else
                {
                    tb_DesiredIP_FirstOct.Text = "";
                    tb_DesiredIP_SecondOct.Text = "";
                    tb_DesiredIP_ThirdOct.Text = ""; 
                    tb_DesiredIP_FourthOct.Text = "";
                }

                //Sets the subnet mask
                if(focusedProfile.SubnetMask != null)
                {
                    string[] Subnet_Octets = focusedProfile.SubnetMask.ToString().Split('.');
                    if(Subnet_Octets.Length == 4)
                    {
                        tb_DesiredSubnetMask_FirstOct.Text = Subnet_Octets[0];
                        tb_DesiredSubnetMask_SecondOct.Text = Subnet_Octets[1];
                        tb_DesiredSubnetMask_ThirdOct.Text = Subnet_Octets[2];
                        tb_DesiredSubnetMask_FourthOct.Text = Subnet_Octets[3];
                    }
                    else
                    {
                        focusedProfile.SubnetMask = null;
                    }
                }
                else
                {
                    tb_DesiredSubnetMask_FirstOct.Text = "";
                    tb_DesiredSubnetMask_SecondOct.Text = "";
                    tb_DesiredSubnetMask_ThirdOct.Text = "";
                    tb_DesiredSubnetMask_FourthOct.Text = "";
                }

                //Sets the default gateway
                if (focusedProfile.DefaultGateway != null)
                {
                    string[] Gateway_Octets = focusedProfile.DefaultGateway.ToString().Split('.');
                    if (Gateway_Octets.Length == 4)
                    {
                        tb_DesiredGateway_FirstOct.Text = Gateway_Octets[0];
                        tb_DesiredGateway_SecondOct.Text = Gateway_Octets[1];
                        tb_DesiredGateway_ThirdOct.Text = Gateway_Octets[2];
                        tb_DesiredGateway_FourthOct.Text = Gateway_Octets[3];
                    }
                    else
                    {
                        focusedProfile.DefaultGateway = null;
                    }
                }
                else
                {
                    tb_DesiredGateway_FirstOct.Text = "";
                    tb_DesiredGateway_SecondOct.Text = "";
                    tb_DesiredGateway_ThirdOct.Text = "";
                    tb_DesiredGateway_FourthOct.Text = "";
                }

                //Sets the primary DNS
                if (focusedProfile.PrimaryDNS != null)
                {
                    string[] PrimaryDNS_Octets = focusedProfile.PrimaryDNS.ToString().Split('.');
                    if (PrimaryDNS_Octets.Length == 4)
                    {
                        tb_DesiredPrimaryDNS_FirstOct.Text = PrimaryDNS_Octets[0];
                        tb_DesiredPrimaryDNS_SecondOct.Text = PrimaryDNS_Octets[1];
                        tb_DesiredPrimaryDNS_ThirdOct.Text = PrimaryDNS_Octets[2];
                        tb_DesiredPrimaryDNS_FourthOct.Text = PrimaryDNS_Octets[3];
                    }
                    else
                    {
                        focusedProfile.PrimaryDNS = null;
                    }
                }
                else
                {
                    tb_DesiredPrimaryDNS_FirstOct.Text = "";
                    tb_DesiredPrimaryDNS_SecondOct.Text = "";
                    tb_DesiredPrimaryDNS_ThirdOct.Text = "";
                    tb_DesiredPrimaryDNS_FourthOct.Text = "";
                }

                //Sets the secondary DNS
                if (focusedProfile.SecondaryDNS != null)
                {
                    string[] SecondaryDNS_Octets = focusedProfile.SecondaryDNS.ToString().Split('.');
                    if (SecondaryDNS_Octets.Length == 4)
                    {
                        tb_DesiredSecondaryDNS_FirstOct.Text = SecondaryDNS_Octets[0];
                        tb_DesiredSecondaryDNS_SecondOct.Text = SecondaryDNS_Octets[1];
                        tb_DesiredSecondaryDNS_ThirdOct.Text = SecondaryDNS_Octets[2];
                        tb_DesiredSecondaryDNS_FourthOct.Text = SecondaryDNS_Octets[3];
                    }
                    else
                    {
                        focusedProfile.SecondaryDNS = null;
                    }
                }
                else
                {
                    tb_DesiredSecondaryDNS_FirstOct.Text = "";
                    tb_DesiredSecondaryDNS_SecondOct.Text = "";
                    tb_DesiredSecondaryDNS_ThirdOct.Text = "";
                    tb_DesiredSecondaryDNS_FourthOct.Text = "";
                }
            }
        }
        #endregion

        #region TV_ContextMenuEvents
        //Defines a new profile in the focused project
        private void Click_ContextMenu_NewProfile(object sender, RoutedEventArgs e)
        {
            newItem_Num = 0;
            foreach(Profile profile in tv_Projects.Items)
            {
                Recursive_TreeView_NewItemLoop(profile, "New Profile");
            }
            if(tv_Projects.SelectedItem != null)
            {
                focusedProfile.Profiles.Add(new Profile("New Profile (" + newItem_Num + ")") { Edit = true , Parent = focusedProfile });
            }

            //Expand the currently selected treeview item to view the newly inserted profile
            selectedItem.IsExpanded = true;
            tv_Projects.UpdateLayout();
        }

        //Imports a profile into the focused project
        private void Click_ContextMenu_Import(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.DefaultExt = ".iprf";
            ofd.Filter = "IP Profile Exports (*.iprf)|*.iprf";

            bool? result = ofd.ShowDialog();

            if (result == true)
            {
                Profile imported = ReadExport(ofd.FileName);
                if (imported != null)
                {
                    if (tv_Projects.SelectedItem != null)
                    {
                        ((Profile)tv_Projects.SelectedItem).Profiles.Add(imported);
                        MessageBox.Show("IP Profile successfully imported!", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("Failed to import IP Profile. File may be corrupted or improper selection. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        //Exports the focused project or profile
        private void Click_ContextMenu_Export(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();

            if (selectedProject)
            {
                sfd.FileName = "IP Project Export";  //Default file name
                sfd.DefaultExt = ".iprj";            //Default file extension
                sfd.Filter = "IP Kung Fu Projects (.iprj)|*.iprj";

                bool? result = sfd.ShowDialog();
                if (result == true)
                {
                    string fileName = sfd.FileName;
                    WriteExport(fileName);
                }
            }
            else
            {
                sfd.FileName = "IP Profile Export";  //Default file name
                sfd.DefaultExt = ".iprf";            //Default file extension
                sfd.Filter = "IP Kung Fu Profiles (.iprf)|*.iprf";

                bool? result = sfd.ShowDialog();
                if (result == true)
                {
                    string fileName = sfd.FileName;
                    WriteExport(fileName);
                }
            }
            
        }

        //Deletes the focused project or profile following a user confirmation
        private void Click_ContextMenu_Delete(object sender, RoutedEventArgs e)
        {
            if(MessageBox.Show("Delete item?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (focusedProfile.Project)
                {
                    tv_Projects.Items.Remove(tv_Projects.SelectedItem);
                }
                else
                {
                    focusedProfile.GetParent().Profiles.Remove(focusedProfile);
                }
            }          
        }

        //Enables/disables context menu buttons depending on if a project or profile is focused
        private void Opening_TreeViewNode_ContextMenu(object sender, ContextMenuEventArgs e)
        {
            if(tv_Projects.SelectedItem == null)
            {
                return;
            }

            if(((Profile)tv_Projects.SelectedItem).Project)
            {
                ContextMenu_NewProfile.IsEnabled = true;
                ContextMenu_Import.IsEnabled = true;

                selectedProject = true;
            }
            else
            {
                ContextMenu_NewProfile.IsEnabled = false;
                ContextMenu_Import.IsEnabled = false;
            }
        }
        #endregion

        #region ComboBoxEvents
        //Do nothing if tab was pressed
        private void PreviewKeyDown_cb_NetworkAdapters(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Tab)
            {
                e.Handled = true;
            }
        }

        //Maintains selected network adapter from combobox
        private void SelectionChanged_cb_NetworkAdapters(object sender, SelectionChangedEventArgs e)
        {
            sel_ni = (NetworkInterface)cb_NetworkAdapters.SelectedItem;

            if (sel_ni != null)
            {
                if(sel_ni.OperationalStatus == OperationalStatus.Up)
                {
                    img_btn_ScanNetwork.Opacity = 1.00;
                    btn_ScanNetwork.IsEnabled = true;
                }
                else
                {
                    img_btn_ScanNetwork.Opacity = .40;
                    btn_ScanNetwork.IsEnabled = false;
                }
            }
        }
        #endregion

        #region TextBoxEvents
        //Allows the user to input only numerical values and properly handles special keys
        private void PreviewKeyDown_tb_VerifyInput(object sender, KeyEventArgs e)
        {
            if (!(e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter || e.Key == Key.Tab || e.Key == Key.OemPeriod || e.Key == Key.Decimal || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Down || e.Key == Key.Up))
            {
                Regex intRegex = new Regex(@"[0123456789]");
                MatchCollection matches = intRegex.Matches(e.Key.ToString());
                if (matches.Count == 0 && e.Key != Key.Enter)
                {
                    e.Handled = true;
                    return;
                }
            }

            //Treat period or decimal as a tab press
            if(e.Key == Key.OemPeriod || e.Key == Key.Decimal)
            {
                e.Handled = true;
                var key = Key.Tab;                                  // Key to send
                var target = Keyboard.PrimaryDevice.ActiveSource;   // Target element
                var routedEvent = Keyboard.KeyDownEvent;            // Event to send
                
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, target, 0, key){ RoutedEvent = routedEvent });
            }   
        }

        //Validates input such that values above 255 are not allowed
        private void TextChanged_tb_ValidateInput(object sender, TextChangedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (!String.IsNullOrEmpty(tb.Text))
            {
                try
                {
                    if (int.Parse(tb.Text) > 255)
                    {
                        MessageBox.Show(tb.Text + " is not a valid entry.  Please enter a value between 0 & 255.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        tb.Text = "223";
                    }
                }
                catch
                {
                    MessageBox.Show("Invalid entry.  Please enter a value between 0 & 255.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        //Handles the "rules" for validating network configuration
        private void LostFocus_tb_ValidateInput(object sender, RoutedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (!String.IsNullOrEmpty(tb.Text))
            {
                //Rules for first Octet of IP Address
                if (tb == tb_DesiredIP_FirstOct || tb == tb_DesiredGateway_FirstOct || tb == tb_DesiredPrimaryDNS_FirstOct || tb == tb_DesiredSecondaryDNS_FirstOct)
                {
                    try
                    {
                        if (int.Parse(tb.Text) > 223 || int.Parse(tb.Text) < 1)
                        {
                            MessageBox.Show(tb.Text + " is not a valid entry.  Please enter a value between 1 & 223.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            tb.Text = "1";
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Invalid entry.  Please enter a value between 1 & 223.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        tb.Text = "";
                    }
                }

                //Rules for fourth Octet of IP Address
                if(tb == tb_DesiredIP_FourthOct)
                {
                    try
                    {
                        if (int.Parse(tb.Text) > 254 || int.Parse(tb.Text) < 1)
                        {
                            MessageBox.Show("The combination of IP Address and Subnet Mask is invalid.  Please enter a valid combination of IP Address and Subnet Mask.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            tb.Text = "1";
                        }
                    }
                    catch
                    {
                        MessageBox.Show("The combination of IP Address and Subnet Mask is invalid.  Please enter a valid combination of IP Address and Subnet Mask.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        tb.Text = "1";
                    }
                }

                //Rules for each Octet of Subnet Mask
                if (tb == tb_DesiredSubnetMask_FirstOct || tb == tb_DesiredSubnetMask_SecondOct || tb == tb_DesiredSubnetMask_ThirdOct || tb == tb_DesiredSubnetMask_FourthOct)
                {
                    try
                    {
                        if (int.Parse(tb.Text) != 0 && int.Parse(tb.Text) != 128 && int.Parse(tb.Text) != 255 )
                        {
                            MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            tb.Text = "0";
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Invalid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        tb.Text = "0";
                    }

                    if (tb == tb_DesiredSubnetMask_FirstOct)
                    {
                        try
                        {
                            if (int.Parse(tb.Text) == 0)
                            {
                                MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                tb.Text = "255";
                            }

                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_SecondOct.Text))
                            {
                                if (int.Parse(tb.Text) < int.Parse(tb_DesiredSubnetMask_SecondOct.Text))
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "255";
                                }
                            }
                        }
                        catch
                        {
                            MessageBox.Show("Invalid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            tb.Text = "255";
                        }
                    }

                    if (tb == tb_DesiredSubnetMask_SecondOct)
                    {
                        try
                        {
                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_FirstOct.Text))
                            {
                                if (int.Parse(tb.Text) > int.Parse(tb_DesiredSubnetMask_FirstOct.Text) || (int.Parse(tb.Text) == 128 && int.Parse(tb_DesiredSubnetMask_FirstOct.Text) == 128))
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "0";
                                }
                            }
                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_ThirdOct.Text))
                            {
                                if (int.Parse(tb.Text) < int.Parse(tb_DesiredSubnetMask_ThirdOct.Text))
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "255";
                                }
                            }
                        }
                        catch
                        {
                            MessageBox.Show("Invalid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            tb.Text = "0";
                        }
                    }

                    if (tb == tb_DesiredSubnetMask_ThirdOct)
                    {
                        try
                        {
                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_SecondOct.Text))
                            {
                                if (int.Parse(tb.Text) > int.Parse(tb_DesiredSubnetMask_SecondOct.Text) || (int.Parse(tb.Text) == 128 && int.Parse(tb_DesiredSubnetMask_SecondOct.Text) == 128))
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "0";
                                }
                            }
                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_FourthOct.Text))
                            {
                                if (int.Parse(tb.Text) < int.Parse(tb_DesiredSubnetMask_FourthOct.Text))
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "255";
                                }
                            }
                        }
                        catch
                        {
                            MessageBox.Show("Invalid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            tb.Text = "0";
                        }
                    }

                    if (tb == tb_DesiredSubnetMask_FourthOct)
                    {
                        try
                        {
                            if (!String.IsNullOrEmpty(tb_DesiredSecondaryDNS_ThirdOct.Text))
                            {
                                if ((int.Parse(tb.Text) > int.Parse(tb_DesiredSubnetMask_ThirdOct.Text) || (int.Parse(tb.Text) == 128 && int.Parse(tb_DesiredSubnetMask_ThirdOct.Text) == 128)) || int.Parse(tb.Text) == 255)
                                {
                                    MessageBox.Show(tb.Text + " is not a valid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    tb.Text = "0";
                                }
                            }
                        }
                        catch
                        {
                            MessageBox.Show("Invalid entry.  The subnet mask must be contiguous.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            tb.Text = "0";
                        }
                    }
                }
            }
        }

        //Selects all text on focus
        private void GotFocus_tb_SelectAll(object sender, RoutedEventArgs e)
        {
            ((TextBox)sender).SelectAll();
        }
        #endregion

        #region ButtonEvents
        private void Click_btn_NewProject(object sender, RoutedEventArgs e)
        {
            newItem_Num = 0;
            foreach (Profile profile in tv_Projects.Items)
            {
                Recursive_TreeView_NewItemLoop(profile, "New Project");
            }

            tv_Projects.Items.Add(new Profile("New Project (" + newItem_Num + ")") { Edit = true, Project = true });            
        }

        private void Click_btn_ImportProject(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.DefaultExt = ".iprj";
            ofd.Filter = "IP Project Exports (*.iprj)|*.iprj";

            bool? result = ofd.ShowDialog();

            if(result == true)
            {
                Profile imported = ReadExport(ofd.FileName);
                if (imported != null)
                {
                    tv_Projects.Items.Add(imported);
                    MessageBox.Show("IP Project successfully imported!", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to import IP Project. File may be corrupted. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Click_btn_ScanAdapter(object sender, RoutedEventArgs e)
        {
            if(sr == null)
            {
                sr = new ScanResults(sel_ni);
                sr.Show();
                sr.Closed += Sr_Closed;
            }
            else
            {
                sr.ScanAdditional(sel_ni);
            }
            
        }

        private void Sr_Closed(object sender, EventArgs e)
        {
            sr = null;
        }

        private void Click_btn_RefreshAdapters(object sender, RoutedEventArgs e)
        {            
            PopulateNetworkAdapters();
        }

        private async void Click_btn_Apply(object sender, RoutedEventArgs e)
        {
            foreach (NetworkInterface ni_ in bgnd_ni)
            {
                if(sel_ni.Id == ni_.Id)
                {
                    continue;
                }

                IPInterfaceProperties ip_prop = ni_.GetIPProperties();
                if (ip_prop == null)
                {
                    continue;
                }
                foreach (UnicastIPAddressInformation ip in ip_prop.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        if (ip.Address != null && focusedProfile.IPAddress != null)
                        {
                            if (focusedProfile.IPAddress.ToString().Equals(ip.Address.ToString()))
                            {
                                MessageBox.Show("IP Address is already set on another adapter.  Please input another address and try again.", "IP Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                        }
                    }
                }
            }

            btn_Apply.IsEnabled = false;
            btn_Apply.Content = "(" + timeout + ")";
            timeElapsed = 0;

            var tokenSource = new CancellationTokenSource();
            ConfigureAdapterTimeout(OnTimerTick, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1000), tokenSource.Token);

            //Progress label definition
            var progress_lbl_Handler = new Progress<string>(value =>
            {
                lbl_Progress.Content = value;
            });
            var progress_lbl = progress_lbl_Handler as IProgress<string>;

            //Progress bar definition
            var progress_bar_Handler = new Progress<double>(value =>
            {
                bar_Progress.Minimum = 0;
                bar_Progress.Maximum = 100;                
                bar_Progress.Value = value;
                bar_Progress.Visibility = Visibility.Visible;
            });
            var progress_bar = progress_bar_Handler as IProgress<double>;

            Task task_configureadapter = Task.Run(() =>
            {
                progress_lbl.Report("Starting...");
                progress_bar.Report(0);

                NetworkManagement nm = new NetworkManagement();                
                
                if (!focusedProfile.IPAuto)
                {
                    //Set gateway
                    try
                    {
                        progress_lbl.Report("Setting gateway...");
                        progress_bar.Report(25);
                        
                        if(focusedProfile.DefaultGateway != null)
                        {
                            nm.setGateway(focusedProfile.DefaultGateway.ToString(), sel_ni.Description);
                        }
                        else
                        {
                            nm.setGateway(null, sel_ni.Description);
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Invalid Gateway, please enter a valid Gateway address and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    try
                    {
                        progress_lbl.Report("Setting IP Address...");
                        progress_bar.Report(40);
                        if(focusedProfile.IPAddress != null && focusedProfile.SubnetMask != null)
                        {
                            nm.setIP(focusedProfile.IPAddress.ToString(), focusedProfile.SubnetMask.ToString(), sel_ni.Description, focusedProfile.IPAuto);
                        }
                        else
                        {
                            nm.setIP(null, null, sel_ni.Description, focusedProfile.IPAuto);
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Invalid IP Address or Subnet Mask, please enter a valid IP Address and Subnet Mask and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    nm.setGateway(null, sel_ni.Description);
                    nm.setIP(null, null, sel_ni.Description, focusedProfile.IPAuto);                    
                }

                progress_lbl.Report("Setting DNS...");
                progress_bar.Report(65);

                if(!focusedProfile.DNSAuto)
                {
                    string DNSs = null;
                    if(focusedProfile.PrimaryDNS != null)
                    {
                        DNSs = focusedProfile.PrimaryDNS.ToString();
                        if(focusedProfile.SecondaryDNS != null)
                        {
                            DNSs = DNSs + "," + focusedProfile.SecondaryDNS.ToString();
                        }
                    }
                    else if(focusedProfile.SecondaryDNS != null)
                    {
                        DNSs = focusedProfile.SecondaryDNS.ToString();
                    }

                    nm.setDNS(DNSs, sel_ni.Description);
                }
                else
                {
                    nm.setDNS(null, sel_ni.Description);
                }

                //If network is connected, but disable and reenable adapter to change network settings, otherwise skip
                if (sel_ni.OperationalStatus.ToString().Equals("Up"))
                {
                    progress_lbl.Report("Disabling adapter...");
                    progress_bar.Report(80);

                    //Disable network adapter
                    nm.SetDisabled(sel_ni.Description);

                    //Await network adapter to return as disabled with timeout
                    int disable_timeout = 8000;
                    Task task_disableNetAdapter = Task.Run(() =>
                    {
                        while (nm.GetNetEnabled(sel_ni.Description))
                        {

                        }
                    });
                    task_disableNetAdapter.Wait(disable_timeout);
                    bool disableComplete = task_disableNetAdapter.IsCompleted;

                    if (!disableComplete)
                    {
                        MessageBox.Show("There was an error disabling network adapter. Verify adapter configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    progress_lbl.Report("Enabling adapter...");
                    progress_bar.Report(90);
                    //Enable network adapter
                    nm.SetEnabled(sel_ni.Description);

                    //Await network adapter to return as enabled with timeout
                    int enable_timeout = 15000;
                    Task task_enableNetAdapter = Task.Run(() =>
                    {
                        bool test = nm.GetNetEnabled(sel_ni.Description);
                        while (!nm.GetNetEnabled(sel_ni.Description))
                        {

                        }
                    });
                    task_enableNetAdapter.Wait(enable_timeout);
                    bool enableComplete = task_enableNetAdapter.IsCompleted;

                    if (!enableComplete)
                    {
                        MessageBox.Show("There was an error enabling network adapter. Verify adapter configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                progress_lbl.Report("Done...");
                progress_bar.Report(100);
                Thread.Sleep(1000);
                progress_lbl.Report("");                
            });

            if (!(await Task.WhenAny(task_configureadapter, Task.Delay(timeout * 1000)) == task_configureadapter))
            {
                MessageBox.Show("Network configuration timeout.  Please verify all adapters are enabled and try again.", "Timeout", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            tokenSource.Cancel();
            btn_Apply.Content = "Apply";
            btn_Apply.IsEnabled = true;
            lbl_Progress.Content = "";
            bar_Progress.Visibility = Visibility.Hidden;
            PopulateNetworkAdapters();
        }
        #endregion

        #region RadioButtonEvents
        private void Checked_rb_IPAuto(object sender, RoutedEventArgs e)
        {
            focusedProfile.IPAuto = true;

            rb_DesiredIP_DNSAuto.IsEnabled = true;

            tb_DesiredIP_FirstOct.IsEnabled = false;
            tb_DesiredIP_SecondOct.IsEnabled = false;
            tb_DesiredIP_ThirdOct.IsEnabled = false;
            tb_DesiredIP_FourthOct.IsEnabled = false;
            tb_DesiredSubnetMask_FirstOct.IsEnabled = false;
            tb_DesiredSubnetMask_SecondOct.IsEnabled = false;
            tb_DesiredSubnetMask_ThirdOct.IsEnabled = false;
            tb_DesiredSubnetMask_FourthOct.IsEnabled = false;
            tb_DesiredGateway_FirstOct.IsEnabled = false;
            tb_DesiredGateway_SecondOct.IsEnabled = false;
            tb_DesiredGateway_ThirdOct.IsEnabled = false;
            tb_DesiredGateway_FourthOct.IsEnabled = false;
        }

        private void Checked_rb_IPManual(object sender, RoutedEventArgs e)
        {
            focusedProfile.IPAuto = false;

            rb_DesiredIP_DNSAuto.IsEnabled = false;
            rb_DesiredIP_DNSManual.IsChecked = true;

            tb_DesiredIP_FirstOct.IsEnabled = true;
            tb_DesiredIP_SecondOct.IsEnabled = true;
            tb_DesiredIP_ThirdOct.IsEnabled = true;
            tb_DesiredIP_FourthOct.IsEnabled = true;
            tb_DesiredSubnetMask_FirstOct.IsEnabled = true;
            tb_DesiredSubnetMask_SecondOct.IsEnabled = true;
            tb_DesiredSubnetMask_ThirdOct.IsEnabled = true;
            tb_DesiredSubnetMask_FourthOct.IsEnabled = true;
            tb_DesiredGateway_FirstOct.IsEnabled = true;
            tb_DesiredGateway_SecondOct.IsEnabled = true;
            tb_DesiredGateway_ThirdOct.IsEnabled = true;
            tb_DesiredGateway_FourthOct.IsEnabled = true;
        }

        private void Checked_rb_DNSAuto(object sender, RoutedEventArgs e)
        {
            focusedProfile.DNSAuto = true;

            tb_DesiredPrimaryDNS_FirstOct.IsEnabled = false;
            tb_DesiredPrimaryDNS_SecondOct.IsEnabled = false;
            tb_DesiredPrimaryDNS_ThirdOct.IsEnabled = false;
            tb_DesiredPrimaryDNS_FourthOct.IsEnabled = false;
            tb_DesiredSecondaryDNS_FirstOct.IsEnabled = false;
            tb_DesiredSecondaryDNS_SecondOct.IsEnabled = false;
            tb_DesiredSecondaryDNS_ThirdOct.IsEnabled = false;
            tb_DesiredSecondaryDNS_FourthOct.IsEnabled = false;
        }

        private void Checked_rb_DNSManual(object sender, RoutedEventArgs e)
        {
            focusedProfile.DNSAuto = false;

            tb_DesiredPrimaryDNS_FirstOct.IsEnabled = true;
            tb_DesiredPrimaryDNS_SecondOct.IsEnabled = true;
            tb_DesiredPrimaryDNS_ThirdOct.IsEnabled = true;
            tb_DesiredPrimaryDNS_FourthOct.IsEnabled = true;
            tb_DesiredSecondaryDNS_FirstOct.IsEnabled = true;
            tb_DesiredSecondaryDNS_SecondOct.IsEnabled = true;
            tb_DesiredSecondaryDNS_ThirdOct.IsEnabled = true;
            tb_DesiredSecondaryDNS_FourthOct.IsEnabled = true;
        }
        #endregion

        #region HelperFunctions
        private void StartUpdate()
        {
            Process.Start(updatePath);
            Close();
        }

        private void CheckForUpdates()
        {
            bool update = false;
            Type type = Type.GetTypeFromProgID("WindowsInstaller.Installer");

            WindowsInstaller.Installer installer = (WindowsInstaller.Installer)

            Activator.CreateInstance(type);

            WindowsInstaller.Database db = installer.OpenDatabase(updatePath, 0);

            WindowsInstaller.View dv = db.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='ProductVersion'");

            WindowsInstaller.Record record = null;

            dv.Execute(record);

            record = dv.Fetch();

            string nv = record.get_StringData(1).ToString();
            string cv = "";
            try
            {
                cv = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }
            catch
            {
                cv = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }

            string[] nv_ = nv.Split('.');
            string[] cv_ = cv.Split('.');
            for (int i = 0; i < nv_.Length; i++)
            {
                if (i == 0)
                {
                    if (Int32.Parse(nv_[i]) > Int32.Parse(cv_[i]))
                    {
                        update = true;
                    }
                }
                else
                {
                    if (Int32.Parse(nv_[i - 1]) == Int32.Parse(cv_[i - 1]) && Int32.Parse(nv_[i]) > Int32.Parse(cv_[i]))
                    {
                        update = true;
                    }
                }
            }

            if (update)
            {
                if (MessageBox.Show("An update exists. Install now?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Update.Report(update);
                }
            }
        }

        private void CreateDirectories()
        {
            if (!Directory.Exists(AppDataFolder + roamingFolder))
            {
                Directory.CreateDirectory(AppDataFolder + roamingFolder);
            }
        }

        private void PopulateNetworkAdapters()
        {
            NetworkInterface prev_ni = (NetworkInterface)cb_NetworkAdapters.SelectedItem;
            cb_NetworkAdapters.DataContext = NetworkInterface.GetAllNetworkInterfaces();
            foreach(object item in cb_NetworkAdapters.Items)
            {
                if(prev_ni.Id == ((NetworkInterface)item).Id)
                {
                    cb_NetworkAdapters.SelectedItem = item;
                    break;
                }
            }
        }

        private void UpdateLabels(string[] lblArray)
        {
            if(!String.IsNullOrEmpty(lblArray[0]))
            {
                lbl_CurrentIP_Status.Content = lblArray[0];
            }
            else
            {
                lbl_CurrentIP_Status.Content = "Status: Down";
            }

            if (!String.IsNullOrEmpty(lblArray[1]))
            {
                lbl_CurrentIP_SSID.Content = lblArray[1];
            }
            else
            {
                lbl_CurrentIP_SSID.Content = "SSID: N/A";
            }

            if (!String.IsNullOrEmpty(lblArray[2]))
            {
                lbl_CurrentIP_Speed.Content = lblArray[2];
            }
            else
            {
                lbl_CurrentIP_Speed.Content = "Speed: N/A"; ;
            }

            if (!String.IsNullOrEmpty(lblArray[3]))
            {
                lbl_CurrentIP_Gateway.Content = lblArray[3];
            }
            else
            {
                lbl_CurrentIP_Gateway.Content = "Default Gateway: N/A"; ;
            }

            if (!String.IsNullOrEmpty(lblArray[4]))
            {
                lbl_CurrentIP_DHCPEnabled.Content = lblArray[4];
            }
            else
            {
                lbl_CurrentIP_DHCPEnabled.Content = "DHCP: Enabled";
            }

            if (!String.IsNullOrEmpty(lblArray[5]))
            {
                lbl_CurrentIP_PrimaryDNS.Content = lblArray[5];
            }
            else
            {
                lbl_CurrentIP_PrimaryDNS.Content = "Primary DNS: N/A";                
            }

            if (!String.IsNullOrEmpty(lblArray[6]))
            {
                lbl_CurrentIP_SecondaryDNS.Content = lblArray[6];
            }
            else
            {
                lbl_CurrentIP_SecondaryDNS.Content = "Secondary DNS: N/A"; ;
            }

            if (!String.IsNullOrEmpty(lblArray[7]))
            {
                lbl_CurrentIP_Address.Content = lblArray[7];
            }
            else
            {
                lbl_CurrentIP_Address.Content = "IP Address: N/A";
            }

            if (!String.IsNullOrEmpty(lblArray[8]))
            {
                lbl_CurrentIP_SubnetMask.Content = lblArray[8];
            }
            else
            {
                lbl_CurrentIP_SubnetMask.Content = "Subnet Mask: N/A";
            }
        }

        private void RefreshIPInfo(NetworkInterface selected)
        {
            string[] labelInfo = new string[20];
            foreach (NetworkInterface ni_ in bgnd_ni)
            {
                if (selected == null)
                {
                    break;
                }

                if (ni_.Description == selected.Description)
                {
                    IPInterfaceProperties ip_prop = ni_.GetIPProperties();
                    if (ip_prop == null)
                    {
                        return;
                    }

                    //Get status of network interface connection
                    labelInfo[0] = "Status: " + ni_.OperationalStatus;

                    //Gets SSID if selected 80211 adapter
                    if (ni_.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && ni_.OperationalStatus == OperationalStatus.Up)
                    {
                        WlanClient wlan = new WlanClient();

                        Collection<String> connectedSsids = new Collection<string>();

                        foreach (WlanClient.WlanInterface wlanInterface in wlan.Interfaces)
                        {
                            Wlan.Dot11Ssid ssid = wlanInterface.CurrentConnection.wlanAssociationAttributes.dot11Ssid;
                            connectedSsids.Add(new String(Encoding.ASCII.GetChars(ssid.SSID, 0, (int)ssid.SSIDLength)));
                        }

                        if (connectedSsids.Count > 0)
                        {
                            labelInfo[1] = "SSID: " + connectedSsids[0];
                        }
                    }
                    else
                    {
                        labelInfo[1] = "SSID: N/A";
                    }

                    //Get speed of network interface if it exists
                    if (ni_.Speed > -1)
                    {
                        if (ni_.Speed > 1000)
                        {
                            if (ni_.Speed > 1000000)
                            {
                                if (ni_.Speed > 1000000000)
                                {
                                    labelInfo[2] = "Speed: " + (ni_.Speed / 1000000000).ToString() + " Gb/s";
                                }
                                else
                                {
                                    labelInfo[2] = "Speed: " + (ni_.Speed / 1000000).ToString() + " Mb/s";
                                }
                            }
                            else
                            {
                                labelInfo[2] = "Speed: " + (ni_.Speed / 1000).ToString() + " Kb/s";
                            }
                        }
                        else
                        {
                            labelInfo[2] = "Speed: " + ni_.Speed.ToString() + " bit/s";
                        }
                    }
                    else
                    {
                        labelInfo[2] = "Speed: N/A";
                    }

                    //Get default gateway if one exists
                    if (ip_prop.GatewayAddresses.Count > 0)
                    {
                        var gateway = ip_prop.GatewayAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (gateway != null)
                        {
                            labelInfo[3] = "Default Gateway: " + gateway.Address.ToString();
                        }
                        else
                        {
                            labelInfo[3] = "Default Gateway: N/A";
                        }
                    }
                    else
                    {
                        labelInfo[3] = "Default Gateway: N/A";
                    }

                    //Gets DHCP status
                    using (NetworkManagement _nm = new NetworkManagement())
                    {
                        if (_nm.IsDHCPEnabled(ni_.Description))
                        {
                            labelInfo[4] = "DHCP: Enabled";
                        }
                        else
                        {
                            labelInfo[4] = "DHCP: Disabled";
                        }
                    }

                    //Gets DNS servers if they exist
                    IPAddressCollection ip_addr = ip_prop.DnsAddresses;
                    if (ip_addr.Count > 0)
                    {
                        labelInfo[5] = "Primary DNS: " + ip_addr[0].ToString();
                        if (ip_addr.Count > 1)
                        {
                            labelInfo[6] = "Secondary DNS: " + ip_addr[1].ToString();
                        }
                        else
                        {
                            labelInfo[6] = "Secondary DNS: N/A";
                        }
                    }
                    else
                    {
                        labelInfo[5] = "Primary DNS: N/A";
                        labelInfo[6] = "Secondary DNS: N/A";
                    }

                    //Get IP Address and Subnet Mask if they exist
                    foreach (UnicastIPAddressInformation ip in ip_prop.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            if (ip.Address != null)
                            {
                                string[] octets = ip.Address.ToString().Split('.');
                                if (octets[0].Equals("169") && ip_prop.UnicastAddresses.Count <= 2)
                                {
                                    continue;
                                }
                                labelInfo[7] = "IP Address: " + ip.Address.ToString();
                                AdapterIPAddress = ip.Address;
                            }
                            else
                            {
                                labelInfo[7] = "IP Address: N/A";
                            }

                            if (ip.IPv4Mask != null)
                            {
                                labelInfo[8] = "Subnet Mask: " + ip.IPv4Mask.ToString();
                            }
                            else
                            {
                                labelInfo[8] = "Subnet Mask: N/A";
                            }
                            break;
                        }
                        else
                        {
                            labelInfo[7] = "IP Address: N/A";
                            labelInfo[8] = "Subnet Mask: N/A";
                            break;
                        }
                    }
                }
            }
            if (selected != null)
            { 
                IP_Labels.Report(labelInfo);
            }
        }

        private Profile ReadExport(string fileName)
        {
            Profile importedProfile;

            if (File.Exists(fileName))
            {
                using (Stream stream = File.Open(fileName, FileMode.Open))
                {
                    var bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

                    importedProfile = (Profile)bformatter.Deserialize(stream);
                }
                return importedProfile;
            }

            return null;
        }

        private void WriteExport(string fileName)
        {
            using (Stream stream = File.Open(fileName, FileMode.Create))
            {
                var bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                bformatter.Serialize(stream, (Profile)tv_Projects.SelectedItem);
            }            
        }

        private void ReadPersistentData()
        {
            if (File.Exists(AppDataFolder + roamingFolder + @"profiles.bin"))
            {
                using (Stream stream = File.Open(AppDataFolder + roamingFolder + @"profiles.bin", FileMode.Open))
                {
                    var bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

                    List<Profile> read_profiles = (List<Profile>)bformatter.Deserialize(stream);
                    foreach (Profile profile in read_profiles)
                    {
                        tv_Projects.Items.Add(profile);
                    }
                }
            }
        }

        private void WritePersistentData()
        {
            List<Profile> write_profiles = new List<Profile>();
            foreach (Profile profile in tv_Projects.Items)
            {
                write_profiles.Add(profile);
            }

            string serializationFile =  AppDataFolder + roamingFolder + @"profiles.bin";

            //serialize
            using (Stream stream = File.Open(serializationFile, FileMode.Create))
            {
                var bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

                bformatter.Serialize(stream, write_profiles);
            }            
        }

        private void Recursive_TreeView_NewItemLoop(Profile prof, string searchFor)
        {
            if (prof.Name.Contains(searchFor))
            {
                string input = prof.Name;
                string output = input.Split('(', ')')[1];
                try
                {
                    if (Convert.ToInt32(output) >= newItem_Num)
                    {
                        newItem_Num = Convert.ToInt32(output) + 1;
                    }
                }
                catch
                {
                    //output cannot be converted to int; continue;
                }
            }

            if (prof.Profiles.Count > 0)
            {
                foreach (Profile nextProf in prof.Profiles)
                {
                    Recursive_TreeView_NewItemLoop(nextProf, searchFor);
                }
            }
        }
        #endregion

        #region Tasks
        // The `onTick` method will be called periodically unless cancelled.
        private static async Task RunPeriodicAsync(Action onTick, TimeSpan dueTime, TimeSpan interval, CancellationToken token)
        {
            // Initial wait time before we begin the periodic loop.
            if (dueTime > TimeSpan.Zero)
            {
                await Task.Delay(dueTime, token);
            }

            // Repeat this loop until cancelled.
            while (!token.IsCancellationRequested)
            {
                // Call our onTick function.
                onTick?.Invoke();

                // Wait to repeat again.
                if (interval > TimeSpan.Zero)
                {
                    await Task.Delay(interval, token);
                }
            }
        }

        private void OnTick()
        {
            try
            {                
                bgnd_ni = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface ni = (NetworkInterface)cb_NetworkAdapters.SelectedItem;

                if (ni == null)
                {
                    return;
                }

                //IP Information Label Definition
                IP_Labels_Handler = new Progress<string[]>(value =>
                {
                    UpdateLabels(value);
                });
                IP_Labels = IP_Labels_Handler as IProgress<string[]>;

                Task.Run(() =>
                {
                    RefreshIPInfo(ni);
                });

                foreach(NetworkInterface ni_ in bgnd_ni)
                {
                    if(ni.Id == ni_.Id)
                    {
                        if (ni_.OperationalStatus == OperationalStatus.Up)
                        {
                            img_btn_ScanNetwork.Opacity = 1.00;
                            btn_ScanNetwork.IsEnabled = true;
                        }
                        else
                        {
                            img_btn_ScanNetwork.Opacity = .40;
                            btn_ScanNetwork.IsEnabled = false;
                        }
                    }
                }                

                if (focusedProfile != null)
                {
                    string IP_Address;
                    string SubnetMask;
                    string DefaultGateway;
                    string PrimaryDNS;
                    string SecondaryDNS;

                    if (rb_DesiredIP_IPManual.IsChecked.Value)
                    {
                        IP_Address = tb_DesiredIP_FirstOct.Text + "." + tb_DesiredIP_SecondOct.Text + "." + tb_DesiredIP_ThirdOct.Text + "." + tb_DesiredIP_FourthOct.Text;
                        SubnetMask = tb_DesiredSubnetMask_FirstOct.Text + "." + tb_DesiredSubnetMask_SecondOct.Text + "." + tb_DesiredSubnetMask_ThirdOct.Text + "." + tb_DesiredSubnetMask_FourthOct.Text;
                        DefaultGateway = tb_DesiredGateway_FirstOct.Text + "." + tb_DesiredGateway_SecondOct.Text + "." + tb_DesiredGateway_ThirdOct.Text + "." + tb_DesiredGateway_FourthOct.Text;

                        try
                        {
                            focusedProfile.IPAddress = IPAddress.Parse(IP_Address);
                        }
                        catch
                        {
                            //Invalid IP Address in textboxes
                            focusedProfile.IPAddress = null;
                        }

                        try
                        {
                            focusedProfile.SubnetMask = IPAddress.Parse(SubnetMask);
                        }
                        catch
                        {
                            //Invalid Subnet Mask in textboxes
                            focusedProfile.SubnetMask = null;
                        }

                        try
                        {
                            focusedProfile.DefaultGateway = IPAddress.Parse(DefaultGateway);
                        }
                        catch
                        {
                            //Invalid Default Gateway in textboxes
                            focusedProfile.DefaultGateway = null;
                        }

                    }
                    else
                    {
                        focusedProfile.IPAddress = null;
                        focusedProfile.SubnetMask = null;
                        focusedProfile.DefaultGateway = null;
                    }

                    if (rb_DesiredIP_DNSManual.IsChecked.Value)
                    {
                        PrimaryDNS = tb_DesiredPrimaryDNS_FirstOct.Text + "." + tb_DesiredPrimaryDNS_SecondOct.Text + "." + tb_DesiredPrimaryDNS_ThirdOct.Text + "." + tb_DesiredPrimaryDNS_FourthOct.Text;
                        SecondaryDNS = tb_DesiredSecondaryDNS_FirstOct.Text + "." + tb_DesiredSecondaryDNS_SecondOct.Text + "." + tb_DesiredSecondaryDNS_ThirdOct.Text + "." + tb_DesiredSecondaryDNS_FourthOct.Text;

                        try
                        {
                            focusedProfile.PrimaryDNS = IPAddress.Parse(PrimaryDNS);
                        }
                        catch
                        {
                            //Invalid primary DNS
                            focusedProfile.PrimaryDNS = null;
                        }

                        try
                        {
                            focusedProfile.SecondaryDNS = IPAddress.Parse(SecondaryDNS);
                        }
                        catch
                        {
                            //Invalid secondary DNS
                            focusedProfile.SecondaryDNS = null;
                        }
                    }
                    else
                    {
                        focusedProfile.PrimaryDNS = null;
                        focusedProfile.SecondaryDNS = null;
                    }

                    if (cb_NetworkAdapters.SelectedItem != null)
                    {
                        focusedProfile.NetworkName = ((NetworkInterface)cb_NetworkAdapters.SelectedItem).Name;
                    }

                    focusedProfile.IPAuto = rb_DesiredIP_IPAuto.IsChecked.Value;
                    focusedProfile.DNSAuto = rb_DesiredIP_DNSAuto.IsChecked.Value;
                }
            }
            catch(Exception e)
            {

            }
        }

        // The `onTick` method will be called periodically unless cancelled.
        private static async Task ConfigureAdapterTimeout(Action onTimerTick, TimeSpan dueTime, TimeSpan interval, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Initial wait time before we begin the periodic loop.
            if (dueTime > TimeSpan.Zero)
            {
                await Task.Delay(dueTime, token);
            }

            // Repeat this loop until cancelled.
            while (!token.IsCancellationRequested)
            {
                // Call our onTick function.
                onTimerTick?.Invoke();

                // Wait to repeat again.
                if (interval > TimeSpan.Zero)
                {
                    await Task.Delay(interval, token);
                }
            }
        }

        private void OnTimerTick()
        {
            timeElapsed++;
            timeout_btn.Report("(" + (timeout - timeElapsed).ToString() + ")");
        }
        #endregion
    }
}

#region ArchivedCode
//p = new Process();
//psi.FileName = "netsh";

//.Arguments = "interface ip set address \"" + focusedProfile.NetworkName + "\" dhcp";

//p.StartInfo = psi;
//p.Start();
//p.Dispose();

//}
//else
//{
//    p = new Process();
//    psi.FileName = "netsh";

//    psi.Arguments = "interface ip set address \"" + focusedProfile.NetworkName + "\" gateway=none";

//    p.StartInfo = psi;
//    p.Start();
//}

////Deletes all DNS Addresses for current network adapter
//p = new Process();
//psi.FileName = "netsh";

//psi.Arguments = "interface ip delete dns \"" + focusedProfile.NetworkName + "\" all";

//p.StartInfo = psi;
//p.Start();

////Sets DNS address if DNS Manual
//p = new Process();
//psi.FileName = "netsh";

//if (!focusedProfile.DNSAuto)
//{
//    if (focusedProfile.PrimaryDNS != null)
//    {
//        psi.Arguments = "interface ip add dns \"" + focusedProfile.NetworkName + "\" " + focusedProfile.PrimaryDNS;
//    }
//}
//else if (focusedProfile.DNSAuto)
//{
//    psi.Arguments = "interface ip set dns \"" + focusedProfile.NetworkName + "\" dhcp";
//}

//p.StartInfo = psi;
//p.Start();

//if (!focusedProfile.DNSAuto)
//{
//    p = new Process();
//    psi.FileName = "netsh";

//    if (focusedProfile.SecondaryDNS != null)
//    {
//        psi.Arguments = "interface ip add dns \"" + focusedProfile.NetworkName + "\" " + focusedProfile.SecondaryDNS + " index=2";

//        p.StartInfo = psi;
//        p.Start();
//    }
//}

////Disable adapter before changing network settings
//p = new Process();
//psi.FileName = "netsh";

//psi.Arguments = "interface set interface \"" + focusedProfile.NetworkName + "\" disabled";

//p.StartInfo = psi;
//p.Start();

//System.Threading.Thread.Sleep(5000);

////Enable adapter following settings changes
//p = new Process();
//psi.FileName = "netsh";

//psi.Arguments = "interface set interface \"" + focusedProfile.NetworkName + "\" enabled";

//p.StartInfo = psi;
//p.Start();

//Set IP Address
//Process p = new Process();
//ProcessStartInfo psi = new ProcessStartInfo();
//psi.FileName = "netsh";

//if (!focusedProfile.IPAuto)
//{
//    psi.Arguments = "interface ip set address \"" + focusedProfile.NetworkName + "\" static " + focusedProfile.IPAddress + " " + focusedProfile.SubnetMask + " " + focusedProfile.DefaultGateway;
//}
//else
//{
//    psi.Arguments = "interface ip set address name=\"" + focusedProfile.NetworkName + "\" source=dhcp";
//}
//p.StartInfo = psi;
//p.Start();

////Deletes DNS for current network adapter
//p = new Process();
//psi = new ProcessStartInfo();
//psi.FileName = "netsh";

//psi.Arguments = "delete dns \"" + focusedProfile.NetworkName + "\" all";                      

//p.StartInfo = psi;
//p.Start();

////Sets DNS address if DNS Manual
//p = new Process();
//psi = new ProcessStartInfo();
//psi.FileName = "netsh";

//if (!focusedProfile.IPAuto)
//{
//    psi.Arguments = "interface ip add dns \"" + focusedProfile.NetworkName + "\" " + focusedProfile.PrimaryDNS + " index=1";
//}
//else if (focusedProfile.DNSAuto)
//{
//    psi.Arguments = "interface ip set dns \"" + focusedProfile.NetworkName + "\" dhcp";
//}

//p.StartInfo = psi;
//p.Start();

//if (!focusedProfile.DNSAuto)
//{
//    p = new Process();
//    psi = new ProcessStartInfo();
//    psi.FileName = "netsh";

//    psi.Arguments = "interface ip add dns \"" + focusedProfile.NetworkName + "\" " + focusedProfile.SecondaryDNS + " index=2";

//    p.StartInfo = psi;
//    p.Start();
//}

//if (!focusedProfile.DNSAuto)
//{
//    nm.setDNS(focusedProfile.PrimaryDNS.ToString() + ",4.4.4.4", ((NetworkInterface)cb_NetworkAdapters.SelectedItem).Description);
//}
#endregion