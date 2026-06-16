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
    public class TrajectoryNameDialog : Form
        {
            private readonly TextBox txtName = new TextBox();
    
            public string TrajectoryName { get; private set; } = "";
    
            public TrajectoryNameDialog()
            {
                Text = "Create trajectory";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(360, 160);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
    
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 3,
                    Padding = new Padding(12)
                };
    
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    
                root.Controls.Add(new Label
                {
                    Text = "Tên:",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleRight
                }, 0, 0);
    
                txtName.Dock = DockStyle.Fill;
                txtName.Text = "";
                root.Controls.Add(txtName, 1, 0);
    
                var note = new Label
                {
                    Text = "Ví dụ: xanh, do, xam",
                    Dock = DockStyle.Fill,
                    ForeColor = Color.DimGray,
                    TextAlign = ContentAlignment.MiddleLeft
                };
    
                root.Controls.Add(note, 1, 1);
    
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft
                };
    
                var btnOk = new Button { Text = "OK", Width = 90 };
                var btnCancel = new Button { Text = "Cancel", Width = 90 };
    
                btnOk.Click += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        MessageBox.Show("Hãy nhập tên quỹ đạo.");
                        return;
                    }
    
                    TrajectoryName = txtName.Text.Trim();
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
    
                root.Controls.Add(buttons, 0, 2);
                root.SetColumnSpan(buttons, 2);
    
                Controls.Add(root);
            }
        }
}
