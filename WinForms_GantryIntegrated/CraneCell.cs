using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GantryCraneIntegrated
{
    public class CraneCell
        {
            public string Name { get; set; }
            public string CommandName { get; set; }
    
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
    
            public bool Mandatory { get; set; }
            public bool SelectedPath { get; set; }
    
            public CraneCell(string name, string commandName, double x, double y, double z, bool mandatory, bool selectedPath)
            {
                Name = name;
                CommandName = commandName;
                X = x;
                Y = y;
                Z = z;
                Mandatory = mandatory;
                SelectedPath = selectedPath;
            }
        }
}
