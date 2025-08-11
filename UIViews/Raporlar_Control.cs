﻿using TekstilScada.Repositories;
using System.Windows.Forms;
using TekstilScada.Properties;
using TekstilScada.Localization;
namespace TekstilScada.UI.Views
{
    public partial class Raporlar_Control : UserControl
    {
        private readonly AlarmReport_Control _alarmReport;
        private readonly ProductionReport_Control _productionReport;
        private readonly OeeReport_Control _oeeReport;
        private readonly TrendAnaliz_Control _trendAnaliz;
        private readonly RecipeOptimization_Control _recipeOptimization;
        private readonly ManualUsageReport_Control _manualUsageReport; // YENİ
        private readonly GenelUretimRaporu_Control _genelUretimRaporu; // YENİ

        public Raporlar_Control()
        {
            InitializeComponent();
            ApplyLocalization();
            _alarmReport = new AlarmReport_Control();
            _productionReport = new ProductionReport_Control();
            _oeeReport = new OeeReport_Control();
            _trendAnaliz = new TrendAnaliz_Control();
            _recipeOptimization = new RecipeOptimization_Control();
            _manualUsageReport = new ManualUsageReport_Control(); // YENİ
            _genelUretimRaporu = new GenelUretimRaporu_Control(); // YENİ
            _genelUretimRaporu.Dock = DockStyle.Fill; // YENİ
            tabPageGenelUretim.Controls.Add(_genelUretimRaporu); // YENİ
            _alarmReport.Dock = DockStyle.Fill;
            tabPageAlarmReport.Controls.Add(_alarmReport);

            _productionReport.Dock = DockStyle.Fill;
            tabPageProductionReport.Controls.Add(_productionReport);

            _oeeReport.Dock = DockStyle.Fill;
            tabPageOeeReport.Controls.Add(_oeeReport);

            _trendAnaliz.Dock = DockStyle.Fill;
            tabPageTrendAnalysis.Controls.Add(_trendAnaliz);

            _recipeOptimization.Dock = DockStyle.Fill;
            tabPageRecipeOptimization.Controls.Add(_recipeOptimization);

            _manualUsageReport.Dock = DockStyle.Fill;
            tabPageManualReport.Controls.Add(_manualUsageReport); // HATA GİDERİLDİ
        }
        private void ApplyLocalization()
        {



            tabPageProductionReport.Text = Resources.üretimraporu;
            tabPageAlarmReport.Text = Resources.alarmrapor;
            tabPageGenelUretim.Text = Resources.geneltüketim;
            tabPageManualReport.Text = Resources.manuelrapor;
            tabPageOeeReport.Text = Resources.OeeReport;

            tabPageRecipeOptimization.Text = Resources.RecipeOptimization;
            tabPageTrendAnalysis.Text = Resources.TrendAnalysis;

        }
        public void InitializeControl(
            MachineRepository machineRepo,
            AlarmRepository alarmRepo,
            ProductionRepository productionRepo,
            DashboardRepository dashboardRepo,
            ProcessLogRepository processLogRepo,
            RecipeRepository recipeRepo)
        {
            _genelUretimRaporu.InitializeControl(machineRepo, productionRepo); // YENİ
            _alarmReport.InitializeControl(machineRepo, alarmRepo);
            _productionReport.InitializeControl(machineRepo, productionRepo, recipeRepo, processLogRepo, alarmRepo);
            _oeeReport.InitializeControl(machineRepo, dashboardRepo);
            _trendAnaliz.InitializeControl(machineRepo, processLogRepo);
            _recipeOptimization.InitializeControl(recipeRepo);
            _manualUsageReport.InitializeControl(machineRepo, processLogRepo); // YENİ
        }
    }
}