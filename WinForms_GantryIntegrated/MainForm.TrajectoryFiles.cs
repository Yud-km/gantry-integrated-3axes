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
private void LoadTrajectoryFiles()
        {
            trajectoryProfiles.Clear();

            string folder = GetTrajectoryFolder();

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            foreach (string filePath in Directory.GetFiles(folder, "*.txt"))
            {
                if (TryLoadTrajectoryFile(filePath, out string name, out List<CraneCell> loadedCells))
                {
                    trajectoryProfiles[name] = loadedCells;
                }
            }
        }

private bool TryLoadTrajectoryFile(string filePath, out string name, out List<CraneCell> loadedCells)
        {
            name = Path.GetFileNameWithoutExtension(filePath);
            loadedCells = new List<CraneCell>();

            try
            {
                string text = File.ReadAllText(filePath, Encoding.UTF8);

                var regex = new Regex(
                    @"\{\s*""(?<name>[^""]+)""\s*,\s*" +
                    @"(?<x>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<y>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<z>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<mandatory>[01])\s*,\s*(?<selected>[01])\s*\}");

                foreach (Match match in regex.Matches(text))
                {
                    string cellName = match.Groups["name"].Value;

                    double x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
                    double y = double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
                    double z = double.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture);

                    bool mandatory = match.Groups["mandatory"].Value == "1";
                    bool selected = match.Groups["selected"].Value == "1";

                    loadedCells.Add(new CraneCell(
                        cellName,
                        cellName,
                        x,
                        y,
                        z,
                        mandatory,
                        selected));
                }

                if (loadedCells.Count != 6)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

private void SaveTrajectoryToFile(string name, List<CraneCell> routeCells)
        {
            string folder = GetTrajectoryFolder();

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string filePath = GetTrajectoryFilePath(name);

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {     
                for (int i = 0; i < routeCells.Count; i++)
                {
                    CraneCell cell = routeCells[i];

                    string comma = (i == routeCells.Count - 1) ? "" : ",";

                    writer.WriteLine(
                        $"{{\"{cell.Name}\", {Number(cell.X)}f, {Number(cell.Y)}f, {Number(cell.Z)}f, {(cell.Mandatory ? 1 : 0)}, {(cell.SelectedPath ? 1 : 0)}}}{comma}");
                }
            }
        }

private string GetTrajectoryFolder()
        {
            /*
             * Files are saved in the folder that contains the running WinForms app.
             * In Visual Studio Debug this is usually bin\\Debug\\net8.0-windows.
             */
            //return Application.StartupPath; // luu truc tiep trong bin 

            //luu theo duong dan 
            string folder = @"D:\GRBL\Gantry_Integrated_Ver1.0\WinForms_GantryIntegrated\trajectory";

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;

        }

private string GetTrajectoryFilePath(string name)
        {
            return Path.Combine(GetTrajectoryFolder(), SanitizeTrajectoryName(name) + ".txt");
        }

private string SanitizeTrajectoryName(string name)
        {
            string result = (name ?? "").Trim();

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            return result;
        }

private void RefreshTrajectoryCombo()
        {
            string old = cboTrajectory.SelectedItem as string;

            isLoadingTrajectoryCombo = true;

            cboTrajectory.Items.Clear();
            cboTrajectory.Items.Add(NoTrajectoryText);

            foreach (string name in trajectoryProfiles.Keys)
            {
                /*
                 * Default đã được add cố định ở trên.
                 * Nếu có Default.txt thì vẫn lưu trong trajectoryProfiles,
                 * nhưng không add lần 2 vào ComboBox.
                 */
                if (!name.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
                {
                    cboTrajectory.Items.Add(name);
                }
            }

            if (!string.IsNullOrWhiteSpace(old) && cboTrajectory.Items.Contains(old))
            {
                cboTrajectory.SelectedItem = old;
            }
            else
            {
                cboTrajectory.SelectedItem = NoTrajectoryText;
            }

            isLoadingTrajectoryCombo = false;

            /* Update UI; if serial is open, also send route to MCU. */
            ApplySelectedTrajectoryNow();
        }
    }
}
