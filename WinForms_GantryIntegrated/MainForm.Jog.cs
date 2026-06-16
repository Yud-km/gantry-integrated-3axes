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
    public partial class MainForm : Form
    {
private Panel BuildJogPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            panel.Controls.Add(root);

            var gridBox = new GroupBox
            {
                Text = "Jog - Chuột phải vào ô để set quỹ đạo / set tọa độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            gridBox.Controls.Add(BuildCellGrid(jogCellButtons, enableContextMenu: true));
            root.Controls.Add(gridBox, 0, 0);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                AutoScroll = true
            };

            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            right.Controls.Add(BuildGotoPanel(), 0, 0);
            right.Controls.Add(BuildJogSettingsPanel(), 0, 1);
            right.Controls.Add(BuildApplyTrajectoryPanel(), 0, 2);
            right.Controls.Add(BuildJogButtonsPanel(), 0, 3);
            right.Controls.Add(BuildRelayPanel(), 0, 4);
            right.Controls.Add(BuildRawPanel(), 0, 5);

            root.Controls.Add(right, 1, 0);

            return panel;
        }

private TableLayoutPanel BuildCellGrid(Dictionary<string, Button> dictionary, bool enableContextMenu)
        {
            dictionary.Clear();

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10)
            };

            for (int i = 0; i < 3; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            }

            for (int i = 0; i < 2; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }

            AddCellButton(grid, dictionary, GetCell("HOME"), 0, 0, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("1"), 1, 0, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("2"), 2, 0, enableContextMenu);

            AddCellButton(grid, dictionary, GetCell("END"), 0, 1, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("4"), 1, 1, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("3"), 2, 1, enableContextMenu);

            UpdateCellViews();

            return grid;
        }

private void AddCellButton(TableLayoutPanel grid, Dictionary<string, Button> dictionary, CraneCell cell, int col, int row, bool enableContextMenu)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                BackColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = cell
            };

            if (enableContextMenu)
            {
                button.ContextMenuStrip = CreateCellContextMenu(button);
            }

            dictionary[cell.Name] = button;
            grid.Controls.Add(button, col, row);
        }

private ContextMenuStrip CreateCellContextMenu(Button button)
        {
            var menu = new ContextMenuStrip();

            var setPath = new ToolStripMenuItem("Set quỹ đạo / bỏ chọn");
            var setCoordinate = new ToolStripMenuItem("Set tọa độ...");
            var setFromCurrent = new ToolStripMenuItem("Lấy tọa độ hiện tại");

            setPath.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                if (cell.Mandatory)
                {
                    MessageBox.Show("HOME và END là ô bắt buộc đi qua.", "Thông báo");
                    return;
                }

                cell.SelectedPath = !cell.SelectedPath;

                SendCommand($"PATH {cell.CommandName} {(cell.SelectedPath ? "ON" : "OFF")}");
                UpdateCellViews();
            };

            setCoordinate.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                using (var dialog = new CoordinateDialog(cell, currentX, currentY, currentZ))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        cell.X = dialog.ResultX;
                        cell.Y = dialog.ResultY;
                        cell.Z = dialog.ResultZ;

                        SendCellToArduino(cell);
                        UpdateCellViews();
                    }
                }
            };

            setFromCurrent.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                cell.X = currentX;
                cell.Y = currentY;
                cell.Z = currentZ;

                SendCellToArduino(cell);
                UpdateCellViews();
            };

            menu.Items.Add(setPath);
            menu.Items.Add(setCoordinate);
            menu.Items.Add(setFromCurrent);

            return menu;
        }

private GroupBox BuildGotoPanel()
        {
            var box = new GroupBox
            {
                Text = "Chạy tới tọa độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 2
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            txtTargetX.Text = "0";
            txtTargetY.Text = "0";
            txtTargetZ.Text = "0";
            txtGotoFeed.Text = "6000";

            layout.Controls.Add(TextLabel("X:"), 0, 0);
            layout.Controls.Add(txtTargetX, 1, 0);

            layout.Controls.Add(TextLabel("Y:"), 2, 0);
            layout.Controls.Add(txtTargetY, 3, 0);

            layout.Controls.Add(TextLabel("Z:"), 0, 1);
            layout.Controls.Add(txtTargetZ, 1, 1);

            layout.Controls.Add(TextLabel("F:"), 2, 1);
            layout.Controls.Add(txtGotoFeed, 3, 1);


            //button goto: di chuyen toi toa do o mode jog
            var btnGoto = new Button
            {
                Text = "GOTO X Y Z",
                Dock = DockStyle.Fill,
                BackColor = Color.SteelBlue,
                ForeColor = Color.White
            };

            btnGoto.Click += (_, _) =>
            {
                if (!CanJogNow()) return;

                SendCommand(
                    $"GOTO X{Number(txtTargetX.Text)} Y{Number(txtTargetY.Text)} Z{Number(txtTargetZ.Text)} F{Number(txtGotoFeed.Text)}");
            };

            layout.Controls.Add(btnGoto, 5, 0);
            layout.SetRowSpan(btnGoto, 2);

            box.Controls.Add(layout);
            return box;
        }

private GroupBox BuildJogSettingsPanel()
        {
            var box = new GroupBox
            {
                Text = "Setting tốc độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3
            };

            for (int i = 0; i < 4; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            txtStep.Text = "5";

            // XY Speed is shared by Jog X/Y and Auto X/Y.
            txtFeed.Text = "6000";

            // Z Speed is shared by Jog Z and Auto Z.
            txtFeedZ.Text = "1800";

            layout.Controls.Add(TextLabel("Step:"), 0, 0);
            layout.Controls.Add(txtStep, 1, 0);

            layout.Controls.Add(TextLabel("XY Speed:"), 2, 0);
            layout.Controls.Add(txtFeed, 3, 0);

            layout.Controls.Add(TextLabel("Z Speed:"), 0, 1);
            layout.Controls.Add(txtFeedZ, 1, 1);

            /*
            var note = new Label
            {
                Text = "XY: Jog X/Y + Auto X/Y    Z: Jog Z + Auto Z",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(note, 2, 1);
            layout.SetColumnSpan(note, 2);

            */

            var btnApply = new Button
            {
                Text = "APPLY SPEED",
                Dock = DockStyle.Fill
            };

            btnApply.Click += (_, _) => ApplySpeed();

            layout.Controls.Add(btnApply, 2, 1);
            layout.SetColumnSpan(btnApply, 1);

            box.Controls.Add(layout);
            return box;
        }

private GroupBox BuildApplyTrajectoryPanel()
        {
            var box = new GroupBox
            {
                Text = "Áp dụng quỹ đạo đang sửa",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1
            };

            var btnApplyRoute = ActionButton("APPLY QUỸ ĐẠO", Color.DarkOrange);
            btnApplyRoute.Click += (_, _) => ApplyCurrentCellsToSelectedTrajectory();

            layout.Controls.Add(btnApplyRoute, 0, 0);
            box.Controls.Add(layout);

            return box;
        }

private GroupBox BuildJogButtonsPanel()
        {
            var box = new GroupBox
            {
                Text = "Jog từng trục",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 3
            };

            for (int i = 0; i < 5; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            }

            for (int i = 0; i < 3; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            }

            grid.Controls.Add(new Label(), 0, 0);
            grid.Controls.Add(new Label(), 1, 0);
            grid.Controls.Add(JogButton("Y+", "Y+"), 2, 0);
            grid.Controls.Add(JogButton("Z+", "Z+"), 3, 0);
            grid.Controls.Add(new Label(), 4, 0);

            grid.Controls.Add(JogButton("X-", "X-"), 0, 1);
            grid.Controls.Add(new Label(), 1, 1);
            grid.Controls.Add(new Label
            {
                Text = "XY",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold)
            }, 2, 1);
            grid.Controls.Add(JogButton("X+", "X+"), 3, 1);
            grid.Controls.Add(new Label(), 4, 1);

            grid.Controls.Add(new Label(), 0, 2);
            grid.Controls.Add(new Label(), 1, 2);
            grid.Controls.Add(JogButton("Y-", "Y-"), 2, 2);
            grid.Controls.Add(JogButton("Z-", "Z-"), 3, 2);

            var btnStop = new Button
            {
                Text = "STOP",
                Dock = DockStyle.Fill,
                BackColor = Color.Firebrick,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };

            btnStop.Click += (_, _) =>
            {
                SendCommand("STOP");
                autoRunning = false;
            };

            grid.Controls.Add(btnStop, 4, 2);

            box.Controls.Add(grid);
            return box;
        }

private GroupBox BuildRelayPanel()
        {
            var box = new GroupBox
            {
                Text = "Điều khiển quạt / nam châm thủ công",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };

            for (int i = 0; i < 4; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            var btnFanOn = new Button { Text = "FAN ON", Dock = DockStyle.Fill };
            btnFanOn.Click += (_, _) => SendCommand("FANON");

            var btnFanOff = new Button { Text = "FAN OFF", Dock = DockStyle.Fill };
            btnFanOff.Click += (_, _) => SendCommand("FANOFF");

            var btnMagOn = new Button { Text = "MAGNET ON", Dock = DockStyle.Fill };
            btnMagOn.Click += (_, _) => SendCommand("MAGON");

            var btnMagOff = new Button { Text = "MAGNET OFF", Dock = DockStyle.Fill };
            btnMagOff.Click += (_, _) => SendCommand("MAGOFF");

            layout.Controls.Add(btnFanOn, 0, 0);
            layout.Controls.Add(btnFanOff, 1, 0);
            layout.Controls.Add(btnMagOn, 2, 0);
            layout.Controls.Add(btnMagOff, 3, 0);

            box.Controls.Add(layout);
            return box;
        }

private GroupBox BuildRawPanel()
        {
            var box = new GroupBox
            {
                Text = "Gửi lệnh thủ công",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                Height = 38
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            txtRaw.Text = "STATUS";
            txtRaw.Dock = DockStyle.Fill;

            var btnSend = new Button
            {
                Text = "SEND",
                Dock = DockStyle.Fill
            };

            btnSend.Click += (_, _) => SendCommand(txtRaw.Text);

            layout.Controls.Add(txtRaw, 0, 0);
            layout.Controls.Add(btnSend, 1, 0);

            box.Controls.Add(layout);
            return box;
        }

private Button JogButton(string text, string command)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };

            btn.Click += (_, _) =>
            {
                if (!CanJogNow()) return;

                ApplySpeed();
                SendCommand(command);
            };

            return btn;
        }

private bool CanJogNow()
        {
            if (autoRunning || machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                machineState.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                machineState.Equals("Homing", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Máy đang chạy. Hãy bấm STOP trước khi Jog.", "Không thể Jog",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

private void ApplySpeed()
        {
            /*
             * Unified speed setting:
             * FEED  = XY speed for Jog X/Y and Auto X/Y.
             * FEEDZ = Z speed for Jog Z and Auto Z.
             */
            SendCommand("STEP " + Number(txtStep.Text));
            SendCommand("FEED " + Number(txtFeed.Text));
            SendCommand("FEEDZ " + Number(txtFeedZ.Text));
        }

private void SyncConfigToArduino()
        {
            ApplySpeed();

            foreach (var cell in cells)
            {
                SendCellToArduino(cell);

                if (!cell.Mandatory)
                {
                    SendCommand($"PATH {cell.CommandName} {(cell.SelectedPath ? "ON" : "OFF")}");
                }
            }
        }

private void SendCellToArduino(CraneCell cell)
        {
            SendCommand(BuildCellCommand(cell));
        }

private void StartAutoWithSelectedTrajectory()
        {
            /* Kept for compatibility. Route is applied by ComboBox change. */
            SendCommand("AUTO");
        }

private string BuildCellCommand(CraneCell cell)
        {
            return $"CELL {cell.CommandName} X{Number(cell.X)} Y{Number(cell.Y)} Z{Number(cell.Z)}";
        }
    }
}
