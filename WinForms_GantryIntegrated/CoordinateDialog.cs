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
    public class CoordinateDialog : Form
        {
            private readonly TextBox txtX = new TextBox();
            private readonly TextBox txtY = new TextBox();
            private readonly TextBox txtZ = new TextBox();
    
            public double ResultX { get; private set; }
            public double ResultY { get; private set; }
            public double ResultZ { get; private set; }
    
            public CoordinateDialog(CraneCell cell, double currentX, double currentY, double currentZ)
            {
                Text = "Set tọa độ ô " + cell.Name;
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(360, 230);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
    
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 5,
                    Padding = new Padding(12)
                };
    
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    
                for (int i = 0; i < 5; i++)
                {
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                }
    
                txtX.Text = cell.X.ToString("0.###", CultureInfo.InvariantCulture);
                txtY.Text = cell.Y.ToString("0.###", CultureInfo.InvariantCulture);
                txtZ.Text = cell.Z.ToString("0.###", CultureInfo.InvariantCulture);
    
                root.Controls.Add(new Label { Text = "X:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
                root.Controls.Add(txtX, 1, 0);
    
                root.Controls.Add(new Label { Text = "Y:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
                root.Controls.Add(txtY, 1, 1);
    
                root.Controls.Add(new Label { Text = "Z:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 2);
                root.Controls.Add(txtZ, 1, 2);
    
                var btnCurrent = new Button { Text = "Lấy tọa độ hiện tại", Dock = DockStyle.Fill };
                btnCurrent.Click += (_, _) =>
                {
                    txtX.Text = currentX.ToString("0.###", CultureInfo.InvariantCulture);
                    txtY.Text = currentY.ToString("0.###", CultureInfo.InvariantCulture);
                    txtZ.Text = currentZ.ToString("0.###", CultureInfo.InvariantCulture);
                };
    
                root.Controls.Add(btnCurrent, 0, 3);
                root.SetColumnSpan(btnCurrent, 2);
    
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft
                };
    
                var btnOk = new Button { Text = "Apply", Width = 90 };
                var btnCancel = new Button { Text = "Cancel", Width = 90 };
    
                btnOk.Click += (_, _) =>
                {
                    double x;
                    double y;
                    double z;
    
                    if (!double.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out x) &&
                        !double.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                    {
                        MessageBox.Show("X không hợp lệ.");
                        return;
                    }
    
                    if (!double.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out y) &&
                        !double.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    {
                        MessageBox.Show("Y không hợp lệ.");
                        return;
                    }
    
                    if (!double.TryParse(txtZ.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out z) &&
                        !double.TryParse(txtZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    {
                        MessageBox.Show("Z không hợp lệ.");
                        return;
                    }
    
                    ResultX = x;
                    ResultY = y;
                    ResultZ = z;
                    DialogResult = DialogResult.OK;
                    Close();
                };
    
                btnCancel.Click += (_, _) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
    
                buttons.Controls.Add(btnOk);
                buttons.Controls.Add(btnCancel);
    
                root.Controls.Add(buttons, 0, 4);
                root.SetColumnSpan(buttons, 2);
    
                Controls.Add(root);
            }
        }
}
