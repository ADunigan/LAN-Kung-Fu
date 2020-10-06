using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace LAN_Kung_Fu
{
    [Serializable]
    public class Profile
    {
        public ObservableCollection<Profile> Profiles { get; set; }
        public IPAddress IPAddress { get; set; }
        public IPAddress SubnetMask { get; set; }
        public IPAddress DefaultGateway { get; set; }
        public IPAddress PrimaryDNS { get; set; }
        public IPAddress SecondaryDNS { get; set; }        
        public bool IPAuto { get; set; }
        public bool DNSAuto { get; set; }
        public string NetworkName { get; set; }
        public string Name { get; set; }
        public bool Edit { get; set; }
        public bool Project { get; set; }
        public Profile Parent { get; set; }

        public Profile(string name)
        {
            this.Name = name;
            this.Profiles = new ObservableCollection<Profile>();
        }

        public Profile()
        {
            
        }

        public Profile GetParent()
        {
            return this.Parent;
        }
    }
}
