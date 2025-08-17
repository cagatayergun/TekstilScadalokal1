// UI/Views/GenelBakis_Control.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TekstilScada.Core;
using TekstilScada.Models;
using TekstilScada.Properties;
using TekstilScada.Repositories;
using TekstilScada.Services;
using TekstilScada.UI.Controls;

namespace TekstilScada.UI.Views
{
    public partial class GenelBakis_Control : UserControl
    {
        private PlcPollingService _pollingService;
        private MachineRepository _machineRepository;
        private DashboardRepository _dashboardRepository;
        private AlarmRepository _alarmRepository;
        private ProcessLogRepository _logRepository;

        private readonly Dictionary<int, DashboardMachineCard_Control> _machineCards = new Dictionary<int, DashboardMachineCard_Control>();
        private System.Windows.Forms.Timer _uiUpdateTimer;

        // YENİ: KPI kartları için özel alanlar ekleyin
        private KpiCard_Control _kpiTotalMachines;
        private KpiCard_Control _kpiRunningMachines;
        private KpiCard_Control _kpiAlarmMachines;
        private KpiCard_Control _kpiIdleMachines;

        public GenelBakis_Control()
        {
            LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
            InitializeComponent();
            // Akıcı çizim için Double Buffering
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            // YENİ: Göz kırpmayı önlemek için FlowLayoutPanel'e Double Buffering uygulayın
            typeof(FlowLayoutPanel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, flpTopKpis, new object[] { true });

            ApplyLocalization();
        }

        public void InitializeControl(PlcPollingService pollingService, MachineRepository machineRepo, DashboardRepository dashboardRepo, AlarmRepository alarmRepo, ProcessLogRepository logRepo)
        {
            _pollingService = pollingService;
            _machineRepository = machineRepo;
            _dashboardRepository = dashboardRepo;
            _alarmRepository = alarmRepo;
            _logRepository = logRepo;
        }
        private void LanguageManager_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void GenelBakis_Control_Load(object sender, EventArgs e)
        {
            if (this.DesignMode) return;

            // YENİ: KPI Kartlarını bir kereliğine oluşturun ve panele ekleyin
            InitializeKpiCards();

            BuildMachineCards();

            _pollingService.OnMachineDataRefreshed += PollingService_OnMachineDataRefreshed;

            _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 2000 }; // 2 saniyede bir güncelleme
            _uiUpdateTimer.Tick += (s, a) => RefreshDashboard();
            _uiUpdateTimer.Start();

            RefreshDashboard(); // İlk yüklemede çalıştır
        }

        // YENİ METOT: KPI kartlarını başlangıçta oluşturur
        private void InitializeKpiCards()
        {
            _kpiTotalMachines = new KpiCard_Control();
            _kpiRunningMachines = new KpiCard_Control();
            _kpiAlarmMachines = new KpiCard_Control();
            _kpiIdleMachines = new KpiCard_Control();

            flpTopKpis.Controls.Add(_kpiTotalMachines);
            flpTopKpis.Controls.Add(_kpiRunningMachines);
            flpTopKpis.Controls.Add(_kpiAlarmMachines);
            flpTopKpis.Controls.Add(_kpiIdleMachines);
        }

        private void BuildMachineCards()
        {
            var allMachines = _machineRepository.GetAllEnabledMachines();
            _machineCards.Clear();
            flpMachineGroups.Controls.Clear();

            var groupedMachines = allMachines
                .OrderBy(m => m.MachineSubType)
                .ThenBy(m => m.Id)
                .GroupBy(m => m.MachineSubType ?? $"{Resources.diger}");

            foreach (var group in groupedMachines)
            {
                var groupPanel = new GroupBox
                {
                    Text = group.Key,
                    Width = flpMachineGroups.Width - 25,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold)
                };

                var innerPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Padding = new Padding(5)
                };

                foreach (var machine in group)
                {
                    var card = new DashboardMachineCard_Control(machine);
                    _machineCards.Add(machine.Id, card);
                    innerPanel.Controls.Add(card);
                }
                groupPanel.Controls.Add(innerPanel);
                flpMachineGroups.Controls.Add(groupPanel);
            }
        }

        private void RefreshDashboard()
        {
            if (this.IsDisposed) return;
            UpdateKpiCards();
            UpdateSidebarCharts();
        }

        private void PollingService_OnMachineDataRefreshed(int machineId, FullMachineStatus status)
        {
            if (_machineCards.TryGetValue(machineId, out var cardToUpdate))
            {
                // Sparkline için son 15 dakikalık veriyi çek
                var trendData = _logRepository.GetLogsForBatch(machineId, status.BatchNumarasi, DateTime.Now.AddMinutes(-15), DateTime.Now);
                cardToUpdate.UpdateData(status, trendData);
            }
        }

        // GÜNCELLENMİŞ METOT: Artık kontrolleri silip yeniden oluşturmuyor
        private void UpdateKpiCards()
        {
            var allStatuses = _pollingService.MachineDataCache.Values;

            int totalMachines = allStatuses.Count;
            int runningMachines = allStatuses.Count(s => s.IsInRecipeMode && !s.HasActiveAlarm);
            int alarmMachines = allStatuses.Count(s => s.HasActiveAlarm);
            int idleMachines = totalMachines - runningMachines - alarmMachines;

            // Mevcut kartların verilerini güncelle
            _kpiTotalMachines.SetData($"{Resources.AllMachines}", totalMachines.ToString(), Color.FromArgb(41, 128, 185));
            _kpiRunningMachines.SetData($"{Resources.aktifüretim}", runningMachines.ToString(), Color.FromArgb(46, 204, 113));
            _kpiAlarmMachines.SetData($"{Resources.alarmdurum}", alarmMachines.ToString(), Color.FromArgb(231, 76, 60));
            _kpiIdleMachines.SetData($"{Resources.bosbekleyen}", idleMachines.ToString(), Color.FromArgb(243, 156, 18));
        }

        private void UpdateSidebarCharts()
        {
            // Saatlik Tüketim Grafiği
            var hourlyData = _dashboardRepository.GetHourlyFactoryConsumption(DateTime.Today);
            formsPlotHourly.Plot.Clear();
            if (hourlyData.Rows.Count > 0)
            {
                // HATA DÜZELTMESİ: Veritabanından gelen değerin DBNull olup olmadığını kontrol et.
                // Eğer null ise 0.0 kullan, değilse değeri double'a çevir.
                double[] hours = hourlyData.AsEnumerable().Select(row => row.IsNull("Saat") ? 0.0 : Convert.ToDouble(row["Saat"])).ToArray();
                double[] consumption = hourlyData.AsEnumerable().Select(row => row.IsNull("ToplamElektrik") ? 0.0 : Convert.ToDouble(row["ToplamElektrik"])).ToArray();

                var barPlot = formsPlotHourly.Plot.Add.Bars(hours, consumption);
                barPlot.Color = ScottPlot.Colors.SteelBlue;
            }
            formsPlotHourly.Plot.Title(Resources.Saatlikelektrik);
            formsPlotHourly.Plot.Axes.AutoScale();
            formsPlotHourly.Refresh();

            // Popüler Alarmlar Grafiği
            var topAlarms = _alarmRepository.GetTopAlarmsByFrequency(DateTime.Now.AddDays(-1), DateTime.Now);
            formsPlotTopAlarms.Plot.Clear();
            if (topAlarms.Any())
            {
                double[] counts = topAlarms.Select(a => (double)a.Count).ToArray();
                var labels = topAlarms.Select(a => a.AlarmText).ToArray();
                var barPlot = formsPlotTopAlarms.Plot.Add.Bars(counts);
                barPlot.Color = ScottPlot.Colors.OrangeRed;
                var ticks = Enumerable.Range(0, labels.Length).Select(i => new ScottPlot.Tick(i, labels[i])).ToArray();
                formsPlotTopAlarms.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
                formsPlotTopAlarms.Plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
            }
            formsPlotTopAlarms.Plot.Title(Resources.ensikalarm);
            formsPlotTopAlarms.Plot.Axes.AutoScale();
            formsPlotTopAlarms.Refresh();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_pollingService != null)
            {
                _pollingService.OnMachineDataRefreshed -= PollingService_OnMachineDataRefreshed;
            }
            _uiUpdateTimer?.Stop();
            _uiUpdateTimer?.Dispose();
            base.OnHandleDestroyed(e);
        }
        public void ApplyLocalization()
        {

            gbHourlyConsumption.Text = Resources.saatlik;

            gbTopAlarms.Text = Resources.son24topalarm;


            //btnSave.Text = Resources.Save;


        }
    }
}