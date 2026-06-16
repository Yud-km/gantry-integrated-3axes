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
private void InitCells()
        {
            cells.Clear();
            cells.AddRange(CreateDefaultCells());
        }

private List<CraneCell> CreateBuiltInDefaultCells()
        {
            return new List<CraneCell>
            {
                new CraneCell("HOME", "HOME", 0, 0, 0, mandatory: true, selectedPath: true),
                new CraneCell("1", "1", 135, 0, 0, mandatory: false, selectedPath: true),
                new CraneCell("2", "2", 270, 0, 0, mandatory: false, selectedPath: true),
                new CraneCell("3", "3", 270, 270, 0, mandatory: false, selectedPath: true),
                new CraneCell("4", "4", 135, 270, 0, mandatory: false, selectedPath: true),
                new CraneCell("END", "END", 0, 270, 0, mandatory: true, selectedPath: true)
            };
        }

private List<CraneCell> CreateDefaultCells()
        {
            /*
             * Default ban đầu là tọa độ cố định trong code.
             * Nếu người dùng đã APPLY khi ComboBox đang chọn Default,
             * chương trình sẽ lưu Default.txt và nạp lại vào trajectoryProfiles.
             * Khi đó Default sẽ lấy dữ liệu đã lưu thay vì hard-code ban đầu.
             */
            if (trajectoryProfiles.TryGetValue(NoTrajectoryText, out List<CraneCell> savedDefault))
            {
                return CloneCells(savedDefault);
            }

            return CreateBuiltInDefaultCells();
        }

private List<CraneCell> CloneCells(IEnumerable<CraneCell> source)
        {
            var result = new List<CraneCell>();

            foreach (var cell in source)
            {
                result.Add(new CraneCell(
                    cell.Name,
                    cell.CommandName,
                    cell.X,
                    cell.Y,
                    cell.Z,
                    cell.Mandatory,
                    cell.SelectedPath));
            }

            return result;
        }

private void ResetSettingCellsToDefaults()
        {
            settingCells.Clear();
            settingCells.AddRange(CreateDefaultCells());
        }

private CraneCell GetCellFromList(List<CraneCell> source, string name)
        {
            foreach (var cell in source)
            {
                if (cell.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    cell.CommandName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Cell not found: " + name);
        }

private CraneCell GetCell(string name)
        {
            foreach (var cell in cells)
            {
                if (cell.Name == name)
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Cell not found: " + name);
        }

private CraneCell GetCellByCommand(string commandName)
        {
            foreach (var cell in cells)
            {
                if (cell.CommandName.Equals(commandName, StringComparison.OrdinalIgnoreCase) ||
                    cell.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase))
                {
                    return cell;
                }
            }

            return null;
        }

private void HighlightCurrentCell(string cellName)
        {
            foreach (var kv in mainCellButtons)
            {
                kv.Value.BackColor = kv.Key.Equals(cellName, StringComparison.OrdinalIgnoreCase)
                    ? Color.LightGreen
                    : Color.White;
            }

            foreach (var kv in jogCellButtons)
            {
                kv.Value.BackColor = kv.Key.Equals(cellName, StringComparison.OrdinalIgnoreCase)
                    ? Color.LightGreen
                    : Color.White;
            }
        }

private void UpdateCellViews()
        {
            UpdateCellButtons(mainCellButtons);
            UpdateCellButtons(jogCellButtons);
        }

private void UpdateCellButtons(Dictionary<string, Button> buttons)
        {
            foreach (var pair in buttons)
            {
                CraneCell cell = GetCell(pair.Key);
                Button button = pair.Value;
                button.Tag = cell;

                string pathMark = cell.SelectedPath ? "✓" : " ";
                if (cell.Mandatory) pathMark = "★";

                button.Text =
                    $"{cell.Name}\n{pathMark}\nX{cell.X:0} Y{cell.Y:0}\nZ{cell.Z:0}";

                if (Math.Abs(currentX - cell.X) <= 1.0 &&
                    Math.Abs(currentY - cell.Y) <= 1.0)
                {
                    button.BackColor = Color.LightGreen;
                }
                else
                {
                    button.BackColor = Color.White;
                }
            }
        }
    }
}
