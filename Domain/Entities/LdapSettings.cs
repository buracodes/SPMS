using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class LdapSettings
    {
        public string Domain { get; set; } 
        public string DirPath { get; set; }
        public string ACDUser { get; set; }
        public string ACDPass { get; set; }
        public string Container { get; set; }   
    }
}
