﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TekstilScada.Core;
using TekstilScada.Models;
using TekstilScada.Repositories;
using TekstilScada.Services;
using TekstilScada.UI.Controls;
using TekstilScada.UI.Controls.RecipeStepEditors;

namespace TekstilScada.UI
{
    public partial class FtpSync_Form : Form
    {
        private readonly MachineRepository _machineRepository;
        private readonly RecipeRepository _recipeRepository;
        private readonly FtpTransferService _transferService;

        // Ön izleme editörü için gerekli değişkenler
        private SplitContainer _byMakinesiEditor;
        private DataGridView dgvRecipeSteps;
        private Panel pnlStepDetails;
        private ScadaRecipe _previewRecipe;

        public FtpSync_Form(MachineRepository machineRepo, RecipeRepository recipeRepo)
        {
            InitializeComponent();
            _machineRepository = machineRepo;
            _recipeRepository = recipeRepo;
            _transferService = FtpTransferService.Instance;
        }

        private void FtpSync_Form_Load(object sender, EventArgs e)
        {
            LoadMachines();
            LoadLocalRecipes();
            SetupTransfersGrid();
            dgvTransfers.DataSource = _transferService.Jobs;
            _transferService.Jobs.ListChanged += Jobs_ListChanged;
        }

        private void LoadMachines()
        {
            var machines = _machineRepository.GetAllEnabledMachines()
                .Where(m => !string.IsNullOrEmpty(m.FtpUsername)).ToList();

            ((ListBox)clbMachines).DataSource = machines;
            ((ListBox)clbMachines).DisplayMember = "DisplayInfo";
            ((ListBox)clbMachines).ValueMember = "Id";
        }

        private void LoadLocalRecipes()
        {
            lstLocalRecipes.DataSource = _recipeRepository.GetAllRecipes();
            lstLocalRecipes.DisplayMember = "RecipeName";
            lstLocalRecipes.ValueMember = "Id";
        }

        private async void LoadHmiRecipes()
        {
            var selectedMachine = clbMachines.CheckedItems.Count == 1
                ? clbMachines.CheckedItems.Cast<Machine>().FirstOrDefault()
                : null;

            if (selectedMachine == null)
            {
                lstHmiRecipes.DataSource = null;
                btnReceive.Enabled = false;
                ClearPreview(); // Ön izlemeyi temizle
                return;
            }

            btnReceive.Enabled = true;

            if (string.IsNullOrEmpty(selectedMachine.VncAddress) || string.IsNullOrEmpty(selectedMachine.FtpUsername))
            {
                MessageBox.Show("Seçilen makine için FTP adresi veya kullanıcı adı tanımlanmamış.", "Eksik Bilgi");
                lstHmiRecipes.DataSource = null;
                return;
            }

            btnRefreshHmi.Enabled = false;
            lstHmiRecipes.DataSource = new List<string> { "Yükleniyor..." };
            ClearPreview();

            try
            {
                var ftpService = new FtpService(selectedMachine.VncAddress, selectedMachine.FtpUsername, selectedMachine.FtpPassword);
                var files = await ftpService.ListDirectoryAsync("/");

                var recipeFiles = files
                    .Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToList();

                lstHmiRecipes.DataSource = recipeFiles;

                // Eğer hiç .csv dosyası bulunamazsa bilgilendirme mesajı göster
                if (!recipeFiles.Any() && files.Any())
                {
                    lstHmiRecipes.DataSource = new List<string> { "FTP'de .csv uzantılı reçete bulunamadı." };
                }
            }
            // GÜNCELLENDİ: Daha spesifik hata yakalama
            catch (WinSCP.SessionRemoteException scpEx)
            {
                // FTP bağlantı hatalarını (zaman aşımı, kimlik doğrulama vb.) özel olarak yakala.
                string errorMessage = $"'{selectedMachine.MachineName}' makinesine bağlanılamadı.\n\n" +
                                      "Lütfen makinenin açık, ağa bağlı ve FTP ayarlarının doğru olduğundan emin olun.\n\n" +
                                      $"Teknik Detay: {scpEx.Message}";
                MessageBox.Show(errorMessage, "FTP Bağlantı Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lstHmiRecipes.DataSource = null; // Hata durumunda listeyi temizle
            }
            catch (Exception ex)
            {
                // Diğer tüm beklenmedik hataları yakala.
                string errorMessage = $"Beklenmedik bir hata oluştu: {ex.Message}";
                MessageBox.Show(errorMessage, "Genel Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lstHmiRecipes.DataSource = null; // Hata durumunda listeyi temizle
            }
            finally
            {
                btnRefreshHmi.Enabled = true;
            }
        }

        private void SetupTransfersGrid()
        {
            dgvTransfers.AutoGenerateColumns = false;
            dgvTransfers.Columns.Clear();
            dgvTransfers.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "MakineAdi", HeaderText = "Makine", FillWeight = 150 });
            dgvTransfers.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ReceteAdi", HeaderText = "Reçete/Dosya", FillWeight = 200 });
            dgvTransfers.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "IslemTipi", HeaderText = "İşlem" });
            dgvTransfers.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Durum", HeaderText = "Durum" });
            dgvTransfers.Columns.Add(new DataGridViewProgressBarColumn { DataPropertyName = "Ilerleme", HeaderText = "İlerleme" });
            dgvTransfers.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HataMesaji", HeaderText = "Hata", FillWeight = 250 });
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            var selectedRecipes = lstLocalRecipes.SelectedItems.Cast<ScadaRecipe>().ToList();
            var selectedMachines = clbMachines.CheckedItems.Cast<Machine>().ToList();

            if (!selectedRecipes.Any() || !selectedMachines.Any())
            {
                MessageBox.Show("Lütfen en az bir reçete ve bir hedef makine seçin.", "Uyarı");
                return;
            }

            _transferService.QueueSendJobs(selectedRecipes, selectedMachines);
        }

        private void btnReceive_Click(object sender, EventArgs e)
        {
            var selectedFiles = lstHmiRecipes.SelectedItems.Cast<string>().ToList();
            var selectedMachine = clbMachines.CheckedItems.Count == 1
                ? clbMachines.CheckedItems.Cast<Machine>().FirstOrDefault()
                : null;

            if (!selectedFiles.Any() || selectedMachine == null)
            {
                MessageBox.Show("Lütfen en az bir HMI dosyası ve listeden SADECE BİR tane kaynak makine seçin.", "Uyarı");
                return;
            }
            _transferService.QueueReceiveJobs(selectedFiles, selectedMachine);
        }

        private void btnRefreshHmi_Click(object sender, EventArgs e)
        {
            LoadHmiRecipes();
        }

        private void clbMachines_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            this.BeginInvoke((MethodInvoker)delegate { LoadHmiRecipes(); });
        }

        private void Jobs_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemChanged)
            {
                var job = _transferService.Jobs[e.NewIndex] as TransferJob;
                if (job != null && job.IslemTipi == TransferType.Al && job.Durum == TransferStatus.Başarılı)
                {
                    LoadLocalRecipes();
                }
            }

            if (this.IsDisposed || !this.IsHandleCreated) return;

            if (dgvTransfers.InvokeRequired)
            {
                dgvTransfers.Invoke(new Action(() => dgvTransfers.Refresh()));
            }
            else
            {
                dgvTransfers.Refresh();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _transferService.Jobs.ListChanged -= Jobs_ListChanged;
            base.OnFormClosing(e);
        }

        #region Ön İzleme Metotları

        private async void lstHmiRecipes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstHmiRecipes.SelectedItems.Count != 1)
            {
                ClearPreview();
                return;
            }

            var selectedFile = lstHmiRecipes.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedFile) || !selectedFile.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                ClearPreview();
                return;
            }
            var selectedMachine = clbMachines.CheckedItems.Cast<Machine>().FirstOrDefault();

            if (selectedFile == null || selectedMachine == null)
            {
                ClearPreview();
                return;
            }

            tabControlMain.SelectedTab = tabPagePreview;
            pnlPreviewArea.Controls.Clear();
            lblPreviewStatus.Visible = true;
            lblPreviewStatus.Text = $"'{selectedFile}' yükleniyor...";

            try
            {
                var ftpService = new FtpService(selectedMachine.VncAddress, selectedMachine.FtpUsername, selectedMachine.FtpPassword);
                string csvContent = await ftpService.DownloadFileAsync($"/{selectedFile}");

                _previewRecipe = RecipeCsvConverter.ToRecipe(csvContent, "temp_preview");
                string previewName = GeneratePreviewRecipeName(selectedMachine, selectedFile, _previewRecipe);

                lblPreviewStatus.Visible = false;
                InitializeBYMakinesiEditor(previewName);
                PopulateStepsGridView();
                pnlPreviewArea.Controls.Add(_byMakinesiEditor);
            }
            catch (Exception ex)
            {
                lblPreviewStatus.Text = $"Ön izleme yüklenemedi: {ex.Message}";
            }
        }

        private void ClearPreview()
        {
            pnlPreviewArea.Controls.Clear();
            pnlPreviewArea.Controls.Add(lblPreviewStatus);
            lblPreviewStatus.Visible = true;
            lblPreviewStatus.Text = "Ön izleme için HMI listesinden bir reçete seçin.";
            _previewRecipe = null;
        }

        private string GeneratePreviewRecipeName(Machine machine, string fileName, ScadaRecipe recipe)
        {
            string machineName = machine.MachineName;
            string recipeNumberPart = "0";
            try
            {
                string fName = Path.GetFileNameWithoutExtension(fileName);
                Match match = Regex.Match(fName, @"\d+$");
                if (match.Success)
                {
                    recipeNumberPart = int.Parse(match.Value).ToString();
                }
            }
            catch { recipeNumberPart = "NO_HATA"; }

            string asciiPart = "BILGI_YOK";
            try
            {
                var step99 = recipe.Steps.FirstOrDefault(s => s.StepNumber == 99);
                if (step99 != null && step99.StepDataWords.Length >= 5)
                {
                    byte[] asciiBytes = new byte[10];
                    for (int i = 0; i < 5; i++)
                    {
                        short word = step99.StepDataWords[i];
                        byte[] wordBytes = BitConverter.GetBytes(word);
                        asciiBytes[i * 2] = wordBytes[0];
                        asciiBytes[i * 2 + 1] = wordBytes[1];
                    }
                    asciiPart = Encoding.ASCII.GetString(asciiBytes).Replace("\0", "").Trim();
                    if (string.IsNullOrEmpty(asciiPart)) asciiPart = "BOS";
                }
                else { asciiPart = "ADIM99_YOK"; }
            }
            catch { asciiPart = "HATA"; }

            return $"{machineName}-{recipeNumberPart}-{asciiPart}";
        }

        private void InitializeBYMakinesiEditor(string recipeName)
        {
            _byMakinesiEditor = new SplitContainer();
            dgvRecipeSteps = new DataGridView();
            pnlStepDetails = new Panel();
            var pnlTopBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.LightSteelBlue };
            var lblRecipeName = new Label { Dock = DockStyle.Fill, Text = recipeName, Font = new Font("Segoe UI", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            pnlTopBar.Controls.Add(lblRecipeName);

            _byMakinesiEditor.Dock = DockStyle.Fill;
            _byMakinesiEditor.SplitterDistance = 450;
            _byMakinesiEditor.Panel1.Controls.Add(dgvRecipeSteps);
            _byMakinesiEditor.Panel2.Controls.Add(pnlStepDetails);
            _byMakinesiEditor.Panel2.Controls.Add(pnlTopBar);

            dgvRecipeSteps.Dock = DockStyle.Fill;
            dgvRecipeSteps.AllowUserToAddRows = false;
            dgvRecipeSteps.AllowUserToDeleteRows = false;
            dgvRecipeSteps.ReadOnly = true;
            dgvRecipeSteps.MultiSelect = false;
            dgvRecipeSteps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRecipeSteps.CellClick += DgvRecipeSteps_CellClick;

            pnlStepDetails.Dock = DockStyle.Fill;
            pnlStepDetails.AutoScroll = true;
            pnlStepDetails.BorderStyle = BorderStyle.FixedSingle;

            SetupStepsGridView();
        }

        private void SetupStepsGridView()
        {
            if (dgvRecipeSteps == null) return;
            dgvRecipeSteps.DataSource = null;
            dgvRecipeSteps.Rows.Clear();
            dgvRecipeSteps.Columns.Clear();
            dgvRecipeSteps.AutoGenerateColumns = false;

            dgvRecipeSteps.Columns.Add(new DataGridViewTextBoxColumn { Name = "StepNumber", HeaderText = "Adım No", DataPropertyName = "StepNumber", Width = 60 });
            dgvRecipeSteps.Columns.Add(new DataGridViewTextBoxColumn { Name = "StepType", HeaderText = "Adım Tipi", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        }

        private void PopulateStepsGridView()
        {
            if (_previewRecipe == null || _previewRecipe.Steps == null || dgvRecipeSteps == null) return;
            dgvRecipeSteps.Rows.Clear();
            foreach (var step in _previewRecipe.Steps)
            {
                string stepTypeName = GetStepTypeName(step);
                dgvRecipeSteps.Rows.Add(step.StepNumber, stepTypeName);
            }
        }

        private string GetStepTypeName(ScadaRecipeStep step)
        {
            var stepTypes = new List<string>();
            if (step.StepDataWords.Length > 24)
            {
                short controlWord = step.StepDataWords[24];
                if ((controlWord & 1) != 0) stepTypes.Add("Su Alma");
                if ((controlWord & 2) != 0) stepTypes.Add("Isıtma");
                if ((controlWord & 4) != 0) stepTypes.Add("Çalışma");
                if ((controlWord & 8) != 0) stepTypes.Add("Dozaj");
                if ((controlWord & 16) != 0) stepTypes.Add("Boşaltma");
                if ((controlWord & 32) != 0) stepTypes.Add("Sıkma");
            }
            return stepTypes.Any() ? string.Join(" + ", stepTypes) : "Tanımsız Adım";
        }

        private void DgvRecipeSteps_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _previewRecipe == null || pnlStepDetails == null) return;

            var selectedMachine = clbMachines.CheckedItems.Cast<Machine>().FirstOrDefault();
            if (selectedMachine == null) return;

            pnlStepDetails.Controls.Clear();

            int selectedIndex = e.RowIndex;
            if (selectedIndex < _previewRecipe.Steps.Count)
            {
                var selectedStep = _previewRecipe.Steps[selectedIndex];
                var mainEditor = new StepEditor_Control();
                mainEditor.LoadStep(selectedStep, selectedMachine);
                mainEditor.SetReadOnly(true);
                mainEditor.Dock = DockStyle.Top;
                mainEditor.AutoSize = true;
                pnlStepDetails.Controls.Add(mainEditor);
            }
        }

        #endregion
        // BU KODU FtpSync_Form.cs DOSYASININ SONUNA EKLEYİN

        // DataGridView için özel ProgressBar kolonu
        public class DataGridViewProgressBarColumn : DataGridViewTextBoxColumn
        {
            public DataGridViewProgressBarColumn()
            {
                this.CellTemplate = new DataGridViewProgressBarCell();
            }
        }

        public class DataGridViewProgressBarCell : DataGridViewTextBoxCell
        {
            protected override void Paint(Graphics g, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
            {
                base.Paint(g, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);

                int progressVal = (value == null) ? 0 : (int)value;
                float percentage = ((float)progressVal / 100.0f);

                if (percentage > 0.0)
                {
                    // İlerleme çubuğunun rengini belirle
                    Brush progressBarBrush = new SolidBrush(Color.FromArgb(180, 220, 180));
                    g.FillRectangle(progressBarBrush, cellBounds.X + 2, cellBounds.Y + 2, Convert.ToInt32((percentage * cellBounds.Width - 4)), cellBounds.Height - 4);
                    progressBarBrush.Dispose();
                }

                // Yüzde metnini yazdır
                string text = progressVal.ToString() + "%";
                SizeF textSize = g.MeasureString(text, cellStyle.Font);
                float textX = cellBounds.X + (cellBounds.Width - textSize.Width) / 2;
                float textY = cellBounds.Y + (cellBounds.Height - textSize.Height) / 2;
                g.DrawString(text, cellStyle.Font, Brushes.Black, textX, textY);
            }
        }
    }
}
