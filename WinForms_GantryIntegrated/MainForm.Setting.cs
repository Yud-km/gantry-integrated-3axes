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
private Panel BuildSettingPanel()
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

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

            panel.Controls.Add(root);

            var gridBox = new GroupBox
            {
                Text = "Setting - tạo quỹ đạo và tọa độ ô",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            gridBox.Controls.Add(BuildSettingCellGrid());
            root.Controls.Add(gridBox, 0, 0);

            var actionBox = new GroupBox
            {
                Text = "Lưu quỹ đạo",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 9,
                Height = 410
            };

            for (int i = 0; i < 9; i++)
            {
                actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            }

            lblSettingRouteName.Text = "Chưa tạo tên quỹ đạo";
            lblSettingRouteName.Dock = DockStyle.Fill;
            lblSettingRouteName.TextAlign = ContentAlignment.MiddleLeft;
            lblSettingRouteName.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            var btnCreate = ActionButton("CREATE", Color.SteelBlue);
            btnCreate.Click += (_, _) => CreateNewTrajectoryDraft();

            var btnAdd = ActionButton("ADD", Color.SeaGreen);
            btnAdd.Click += (_, _) => SaveCurrentTrajectoryDraft();

            var btnDelete = ActionButton("DELETE SELECTED ROUTE", Color.Firebrick);
            btnDelete.Click += (_, _) => DeleteSelectedTrajectory();

            var btnResetDraft = new Button
            {
                Text = "RESET DEFAULT CELLS",
                Dock = DockStyle.Fill
            };
            btnResetDraft.Click += (_, _) =>
            {
                ResetSettingCellsToDefaults();
                UpdateSettingCellViews();
            };

            var note = new Label
            {
                Text =
                    "Cách dùng: CREATE đặt tên → chuột phải vào ô để set tọa độ/quỹ đạo → ADD để lưu file .txt. " +
                    "Chọn quỹ đạo trên ComboBox rồi DELETE để xóa quỹ đạo và file .txt. Default không xóa được.",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.TopLeft
            };

            actions.Controls.Add(lblSettingRouteName, 0, 0);
            actions.Controls.Add(btnCreate, 0, 1);
            actions.Controls.Add(btnAdd, 0, 2);
            actions.Controls.Add(btnDelete, 0, 3);
            actions.Controls.Add(btnResetDraft, 0, 4);
            actions.Controls.Add(note, 0, 5);
            actions.SetRowSpan(note, 3);

            actionBox.Controls.Add(actions);
            root.Controls.Add(actionBox, 1, 0);

            return panel;
        }

private TableLayoutPanel BuildSettingCellGrid()
        {
            settingCellButtons.Clear();

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

            AddSettingCellButton(grid, GetCellFromList(settingCells, "HOME"), 0, 0);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "1"), 1, 0);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "2"), 2, 0);

            AddSettingCellButton(grid, GetCellFromList(settingCells, "END"), 0, 1);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "4"), 1, 1);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "3"), 2, 1);

            UpdateSettingCellViews();

            return grid;
        }

private void AddSettingCellButton(TableLayoutPanel grid, CraneCell cell, int col, int row)
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

            button.ContextMenuStrip = CreateSettingCellContextMenu(button);

            settingCellButtons[cell.Name] = button;
            grid.Controls.Add(button, col, row);
        }

private ContextMenuStrip CreateSettingCellContextMenu(Button button)
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
                UpdateSettingCellViews();
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

                        UpdateSettingCellViews();
                    }
                }
            };

            setFromCurrent.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                cell.X = currentX;
                cell.Y = currentY;
                cell.Z = currentZ;

                UpdateSettingCellViews();
            };

            menu.Items.Add(setPath);
            menu.Items.Add(setCoordinate);
            menu.Items.Add(setFromCurrent);

            return menu;
        }

private void UpdateSettingCellViews()
        {
            foreach (var pair in settingCellButtons)
            {
                CraneCell cell = GetCellFromList(settingCells, pair.Key);
                Button button = pair.Value;
                button.Tag = cell;

                string pathMark = cell.SelectedPath ? "✓" : " ";
                if (cell.Mandatory) pathMark = "★";

                button.Text =
                    $"{cell.Name}\n{pathMark}\nX{cell.X:0} Y{cell.Y:0}\nZ{cell.Z:0}";

                /*
                 * Setting screen is only for route editing.
                 * Do not change color when crane reaches the cell.
                 */
                button.BackColor = Color.White;
            }
        }

private void CreateNewTrajectoryDraft()
        {
            using (var dialog = new TrajectoryNameDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string name = SanitizeTrajectoryName(dialog.TrajectoryName);

                if (name.Length == 0)
                {
                    MessageBox.Show("Tên quỹ đạo không hợp lệ.", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string filePath = GetTrajectoryFilePath(name);

                if (File.Exists(filePath))
                {
                    DialogResult overwrite = MessageBox.Show(
                        "Quỹ đạo này đã tồn tại. Bạn có muốn ghi đè khi bấm ADD không?",
                        "Quỹ đạo đã tồn tại",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (overwrite != DialogResult.Yes)
                    {
                        return;
                    }
                }

                currentSettingTrajectoryName = name;
                ResetSettingCellsToDefaults();
                UpdateSettingCellViews();

                lblSettingRouteName.Text = "Đang tạo: " + currentSettingTrajectoryName;
            }
        }

private void ApplyCurrentCellsToSelectedTrajectory()
        {
            string selectedName = cboTrajectory.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedName))
            {
                selectedName = NoTrajectoryText;
            }

            /*
             * Chỉ cho APPLY khi máy rảnh. Nếu máy đang Auto/Home/Homing/Jog
             * thì không cập nhật quỹ đạo để tránh thay đổi dữ liệu giữa lúc chạy.
             */
            if (serial.IsOpen &&
                (autoRunning ||
                 machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Homing", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Jog", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Máy đang chạy. Chỉ APPLY quỹ đạo khi State = Idle.",
                    "Không thể APPLY",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<CraneCell> updatedCells = CloneCells(cells);

            /*
             * HOME và END luôn bắt buộc. Dòng này bảo vệ dữ liệu nếu người dùng
             * hoặc file text sửa nhầm selectedPath của HOME/END.
             */
            foreach (var cell in updatedCells)
            {
                if (cell.Mandatory)
                {
                    cell.SelectedPath = true;
                }
            }

            try
            {
                /*
                 * Nếu ComboBox đang là Default thì vẫn cho lưu Default.txt.
                 * Lần sau chọn Default hoặc khởi động lại app, Default sẽ lấy
                 * tọa độ/quỹ đạo đã APPLY thay vì dùng mặc định hard-code.
                 */
                trajectoryProfiles[selectedName] = CloneCells(updatedCells);
                SaveTrajectoryToFile(selectedName, updatedCells);

                string oldSelected = selectedName;
                RefreshTrajectoryCombo();
                cboTrajectory.SelectedItem = oldSelected;

                /*
                 * Gửi lại ROUTE + CELL + PATH + APPLYROUTE xuống Arduino ngay nếu đang kết nối.
                 * Khi MCU trả về route:applied thì routeApplyPending sẽ được xóa.
                 */
                if (serial.IsOpen)
                {
                    routeApplyPending = true;
                    QueueCommands(BuildRouteCommands(selectedName, updatedCells));
                    Log("Route applying to MCU: " + selectedName);
                }
                else
                {
                    Log("Route applied locally: " + selectedName);
                }

                UpdateCellViews();

                MessageBox.Show(
                    serial.IsOpen
                        ? "Đã lưu file và đang gửi quỹ đạo xuống vi điều khiển. Hãy đợi log RX< route:applied rồi START AUTO."
                        : "Đã APPLY và lưu quỹ đạo: " + selectedName,
                    "APPLY OK",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể APPLY quỹ đạo.\n" + ex.Message,
                    "APPLY ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

private void SaveCurrentTrajectoryDraft()
        {
            if (string.IsNullOrWhiteSpace(currentSettingTrajectoryName))
            {
                MessageBox.Show("Hãy bấm CREATE và đặt tên quỹ đạo trước.", "Thiếu tên quỹ đạo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveTrajectoryToFile(currentSettingTrajectoryName, settingCells);

            trajectoryProfiles[currentSettingTrajectoryName] = CloneCells(settingCells);
            RefreshTrajectoryCombo();
            cboTrajectory.SelectedItem = currentSettingTrajectoryName;

            MessageBox.Show(
                "Đã lưu quỹ đạo: " + currentSettingTrajectoryName,
                "ADD OK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

private void DeleteSelectedTrajectory()
        {
            string selectedName = cboTrajectory.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedName))
            {
                MessageBox.Show("Chưa chọn quỹ đạo để xóa.", "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (selectedName.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Không được xóa quỹ đạo Default.", "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!trajectoryProfiles.ContainsKey(selectedName))
            {
                MessageBox.Show("Không tìm thấy quỹ đạo: " + selectedName, "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "Bạn có chắc muốn xóa quỹ đạo '" + selectedName + "' không?\n" +
                "File .txt của quỹ đạo này cũng sẽ bị xóa.",
                "Xác nhận xóa quỹ đạo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            try
            {
                string filePath = GetTrajectoryFilePath(selectedName);

                trajectoryProfiles.Remove(selectedName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                if (currentSettingTrajectoryName.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    currentSettingTrajectoryName = "";
                    lblSettingRouteName.Text = "Chưa tạo tên quỹ đạo";
                    ResetSettingCellsToDefaults();
                    UpdateSettingCellViews();
                }

                /* Sau khi xóa, quay về Default để tránh ComboBox còn chọn quỹ đạo không tồn tại. */
                RefreshTrajectoryCombo();
                cboTrajectory.SelectedItem = NoTrajectoryText;

                MessageBox.Show(
                    "Đã xóa quỹ đạo: " + selectedName,
                    "DELETE OK",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể xóa quỹ đạo.\n" + ex.Message,
                    "DELETE ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
