// MainForm.cs
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TekstilScada.Core;
using TekstilScada.Localization;
using TekstilScada.Models;
using TekstilScada.Repositories;
using TekstilScada.Services;
using TekstilScada.UI;
using TekstilScada.UI.Controls;
using TekstilScada.UI.Views;
using TekstilScada.Properties;

namespace TekstilScada
{
    public partial class MainForm : Form
    {
        // Repository ve Servisler
        private readonly MachineRepository _machineRepository;
        private readonly RecipeRepository _recipeRepository;
        private readonly ProcessLogRepository _processLogRepository;
        private readonly AlarmRepository _alarmRepository;
        private readonly ProductionRepository _productionRepository;
        private readonly PlcPollingService _pollingService;
        private readonly DashboardRepository _dashboardRepository;

        // Aray�z Kontrolleri (Views)
        private readonly Proses�zleme_Control _prosesIzlemeView;
        private readonly ProsesKontrol_Control _prosesKontrolView;
        private readonly Ayarlar_Control _ayarlarView;
        private readonly MakineDetay_Control _makineDetayView;
        private readonly Raporlar_Control _raporlarView;
        private readonly LiveEventPopup_Form _liveEventPopup;
        private readonly GenelBakis_Control _genelBakisView;

        private VncViewer_Form _activeVncViewerForm = null;

        public MainForm()
        {
            InitializeComponent();

            // 1. ADIM: T�m nesneler burada olu�turulur.
            // Bu, NullReferenceException hatas�n� �nlemek i�in kritiktir.
            _machineRepository = new MachineRepository();
            _recipeRepository = new RecipeRepository();
            _processLogRepository = new ProcessLogRepository();
            _alarmRepository = new AlarmRepository();
            _productionRepository = new ProductionRepository();
            _pollingService = new PlcPollingService(_alarmRepository, _processLogRepository, _productionRepository, _recipeRepository);
            _dashboardRepository = new DashboardRepository();

            _prosesIzlemeView = new Proses�zleme_Control();
            _prosesKontrolView = new ProsesKontrol_Control();
            _ayarlarView = new Ayarlar_Control();
            _makineDetayView = new MakineDetay_Control();
            _raporlarView = new Raporlar_Control();
            _liveEventPopup = new LiveEventPopup_Form();
            _genelBakisView = new GenelBakis_Control();

            // Olay abonelikleri (Events)
            LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
            _ayarlarView.MachineListChanged += OnMachineListChanged;
            _pollingService.OnActiveAlarmStateChanged += OnActiveAlarmStateChanged;
            _prosesIzlemeView.MachineDetailsRequested += OnMachineDetailsRequested;
            _prosesIzlemeView.MachineVncRequested += OnMachineVncRequested;
            _makineDetayView.BackRequested += OnBackRequested;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 2. ADIM: Form tamamen y�klendikten sonra bu metot �al���r.
            // Veritaban� ve PLC i�lemlerini ba�latan metotlar burada �a�r�l�r.
            ApplyLocalization();
            UpdateUserInfoAndPermissions();
            ReloadSystem(_genelBakisView);
            ApplyPermissions(); // YEN�: Yetkileri uygula
        }
        private void ApplyPermissions()
        {
            // Raporlar butonunu sadece yetkisi olanlar g�rebilir/kullanabilir.
            btnRaporlar.Enabled = PermissionService.CanViewReports;

            // Proses Kontrol (Re�ete) butonunu yetkisi olanlar kullanabilir.
            btnProsesKontrol.Enabled = PermissionService.CanEditRecipes;

            // Ayarlar butonunu sadece Admin g�rebilir/kullanabilir.
            btnAyarlar.Enabled = PermissionService.CanViewSettings;
        }
        private void ReloadSystem(Control viewToShow)
        {
            _pollingService.Stop();
            List<Machine> machines = _machineRepository.GetAllEnabledMachines();
            if (machines == null)
            {
                MessageBox.Show(Resources.DatabaseConnectionFailed, Resources.CriticalError, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _pollingService.Start(machines);
            var plcManagers = _pollingService.GetPlcManagers();

            // Kontrolleri en g�ncel verilerle ba�lat
            _prosesIzlemeView.InitializeView(machines, _pollingService);
            _prosesKontrolView.InitializeControl(_recipeRepository, _machineRepository, plcManagers);
            _ayarlarView.InitializeControl(_machineRepository, plcManagers);
            _raporlarView.InitializeControl(_machineRepository, _alarmRepository, _productionRepository, _dashboardRepository, _processLogRepository, _recipeRepository);
            _genelBakisView.InitializeControl(_pollingService, _machineRepository, _dashboardRepository, _alarmRepository, _processLogRepository);

            // HANG� SAYFANIN G�STER�LECE��N� KONTROL ET
            if (viewToShow != _genelBakisView)
            {
                // E�er bir sayfa belirtilmi�se onu g�ster
                ShowView(_ayarlarView);
            }
            else
            {
                // E�er bir sayfa belirtilmemi�se (ilk a��l�� gibi), Genel Bak��'� g�ster
                ShowView(_genelBakisView);
            }
        }

        #region Aray�z ve Dil Y�netimi

        private void LanguageManager_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
            UpdateUserInfoAndPermissions();
        }

        private void ApplyLocalization()
        {
            // Ana form ba�l��� ve ana men� butonlar� "Strings" s�n�f�ndan geliyor.
            this.Text = TekstilScada.Localization.Strings.ApplicationTitle;
            btnGenelBakis.Text = TekstilScada.Localization.Strings.MainMenu_GeneralOverview;
            btnProsesIzleme.Text = TekstilScada.Localization.Strings.MainMenu_ProcessMonitoring;
            btnProsesKontrol.Text = TekstilScada.Localization.Strings.MainMenu_ProcessControl;
            btnRaporlar.Text = TekstilScada.Localization.Strings.MainMenu_Reports;
            btnAyarlar.Text = TekstilScada.Localization.Strings.MainMenu_Settings;

            // Men� ve durum �ubu�u gibi di�er elemanlar "Resources" s�n�f�ndan geliyor.
            dilToolStripMenuItem.Text = Resources.Language;
            oturumToolStripMenuItem.Text = Resources.Session;
            ��k��YapToolStripMenuItem.Text = Resources.Logout;
            lblStatusLiveEvents.Text = Resources.Livelogsee;
        }

        private void UpdateUserInfoAndPermissions()
        {
            if (CurrentUser.IsLoggedIn)
            {
                lblStatusCurrentUser.Text = $"{Resources.Loggedin}: {CurrentUser.User.FullName}";
            }
            else
            {
                lblStatusCurrentUser.Text = $"{Resources.Loggedin}: -";
            }
            // Ayarlar butonunu sadece "Admin" rol�ne sahip kullan�c�lar i�in etkinle�tir.
            btnAyarlar.Enabled = CurrentUser.HasRole("Admin");
        }

        private void ShowView(UserControl view)
        {
            if (view is Ayarlar_Control && !PermissionService.CanViewSettings)

            {
                MessageBox.Show(Resources.NoAccess, Resources.AccessDenied);
                return;
            }
            if (view is Raporlar_Control && !PermissionService.CanViewReports)
            {
                MessageBox.Show(Resources.NoAccess, Resources.AccessDenied);
                return;
            }
            pnlContent.Controls.Clear();
            view.Dock = DockStyle.Fill;
            pnlContent.Controls.Add(view);
        }

        #endregion

        #region Olay Y�neticileri (Event Handlers)

        private void OnMachineListChanged(object sender, EventArgs e) => ReloadSystem(_ayarlarView);
        private void OnBackRequested(object sender, EventArgs e) => ShowView(_prosesIzlemeView);
        private void btnGenelBakis_Click(object sender, EventArgs e) => ShowView(_genelBakisView);
        private void btnProsesIzleme_Click(object sender, EventArgs e) => ShowView(_prosesIzlemeView);
        private void btnProsesKontrol_Click(object sender, EventArgs e) => ShowView(_prosesKontrolView);
        private void btnRaporlar_Click(object sender, EventArgs e) => ShowView(_raporlarView);
        private void btnAyarlar_Click(object sender, EventArgs e) => ShowView(_ayarlarView);
        private void t�rk�eToolStripMenuItem_Click(object sender, EventArgs e) => LanguageManager.SetLanguage("tr-TR");
        private void englishToolStripMenuItem_Click(object sender, EventArgs e) => LanguageManager.SetLanguage("en-US");

        private void ��k��YapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(Resources.Cikiseminmisin, Resources.Confirim, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                this.Hide();
                _pollingService.Stop();

                using (var loginForm = new LoginForm())
                {
                    if (loginForm.ShowDialog() == DialogResult.OK)
                    {
                        UpdateUserInfoAndPermissions();
                        ReloadSystem(_genelBakisView);
                        this.Show();
                    }
                    else
                    {
                        Application.Exit();
                    }
                }
            }
        }

        private void OnMachineDetailsRequested(object sender, int machineId)
        {
            var machine = _machineRepository.GetAllMachines().FirstOrDefault(m => m.Id == machineId);
            if (machine != null)
            {
                // YEN�: _productionRepository parametresini ekleyin
                _makineDetayView.InitializeControl(machine, _pollingService, _processLogRepository, _alarmRepository, _recipeRepository, _productionRepository);
                ShowView(_makineDetayView);
            }
        }
        private void OnMachineVncRequested(object sender, int machineId)
        {
            if (_activeVncViewerForm != null && !_activeVncViewerForm.IsDisposed)
            {
                _activeVncViewerForm.Activate();
                MessageBox.Show(Resources.Vnccurrentclose, Resources.Warning, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var machine = _machineRepository.GetAllMachines().FirstOrDefault(m => m.Id == machineId);
            if (machine != null && !string.IsNullOrEmpty(machine.VncAddress))
            {
                try
                {
                    var vncForm = new VncViewer_Form(machine.VncAddress, machine.VncPassword);
                    vncForm.Text = $"{machine.MachineName} - {Resources.VncConnectionTo}";
                    vncForm.FormClosed += (s, args) => { _activeVncViewerForm = null; };
                    _activeVncViewerForm = vncForm;
                    vncForm.Show();
                }
                catch (Exception ex)
                {
                    _activeVncViewerForm = null;
                    MessageBox.Show($"{Resources.Vncconnecterror} {ex.Message}", Resources.Error);
                }
            }
            else
            {
                MessageBox.Show(Resources.Vncnomachine, Resources.Information);
            }
        }

        private void OnActiveAlarmStateChanged(int machineId, FullMachineStatus status)
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;
            this.Invoke(new Action(() =>
            {
                if (this.IsDisposed) return;

                var activeAlarms = _pollingService.MachineDataCache.Values.Where(s => s.HasActiveAlarm).ToList();

                if (activeAlarms.Any())
                {
                    var alarmToShow = activeAlarms
                        .Select(s => new { Status = s, Definition = _alarmRepository.GetAlarmDefinitionByNumber(s.ActiveAlarmNumber) })
                        .Where(ad => ad.Definition != null)
                        .OrderByDescending(ad => ad.Definition.Severity)
                        .FirstOrDefault();

                    if (alarmToShow != null)
                    {
                        lblStatusLiveEvents.Text = $"[{alarmToShow.Status.MachineName}] - ALARM: {alarmToShow.Definition.AlarmText}";
                        lblStatusLiveEvents.BackColor = Color.FromArgb(231, 76, 60); // K�rm�z�
                        lblStatusLiveEvents.ForeColor = Color.White;
                    }
                }
                else
                {
                   
                    lblStatusLiveEvents.BackColor = System.Drawing.SystemColors.Control;
                    lblStatusLiveEvents.ForeColor = System.Drawing.SystemColors.ControlText;
                }
            }));
        }

        private void lblStatusLiveEvents_Click(object sender, EventArgs e)
        {
            if (_liveEventPopup.Visible)
            {
                _liveEventPopup.Hide();
            }
            else
            {
                _liveEventPopup.Show(this);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
            _pollingService.Stop();
            if (_activeVncViewerForm != null && !_activeVncViewerForm.IsDisposed)
            {
                try
                {
                    _activeVncViewerForm.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"{Resources.Closeandvnc} {ex.Message}");
                }
            }
        }

        #endregion
    }
}