// TekstilScada.Core/Services/LiveStepAnalyzer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using TekstilScada.Models;

namespace TekstilScada.Core.Services
{
    public class LiveStepAnalyzer
    {
        private readonly ScadaRecipe _recipe;
        private int _currentStepNumber = 0;
        private DateTime _currentStepStartTime;
        private DateTime? _currentPauseStartTime;
        private double _currentStepPauseSeconds = 0;

        public List<ProductionStepDetail> AnalyzedSteps { get; }
        public ScadaRecipe Recipe { get; private set; }
        public DateTime CurrentStepStartTime { get; private set; }

        private const double SECONDS_PER_LITER = 0.5;

        public LiveStepAnalyzer(ScadaRecipe recipe)
        {
            _recipe = recipe;
            this.Recipe = recipe;
            AnalyzedSteps = new List<ProductionStepDetail>();
            CurrentStepStartTime = DateTime.Now;
        }

        public bool ProcessData(FullMachineStatus status)
        {
            bool hasStepChanged = false;

            if (status.IsPaused && !_currentPauseStartTime.HasValue)
            {
                _currentPauseStartTime = DateTime.Now;
            }
            else if (!status.IsPaused && _currentPauseStartTime.HasValue)
            {
                _currentStepPauseSeconds += (DateTime.Now - _currentPauseStartTime.Value).TotalSeconds;
                _currentPauseStartTime = null;
            }

            if (status.AktifAdimNo != _currentStepNumber)
            {
                if (_currentStepNumber > 0)
                {
                    FinalizeStep(_currentStepNumber);
                }

                HandleSkippedSteps(status, _currentStepNumber + 1, status.AktifAdimNo);
                StartNewStep(status);
                hasStepChanged = true;
            }

            return hasStepChanged;
        }

        private void StartNewStep(FullMachineStatus status)
        {
            _currentStepNumber = status.AktifAdimNo;
            CurrentStepStartTime = DateTime.Now;
            _currentPauseStartTime = null;
            _currentStepPauseSeconds = 0;

            var recipeStep = _recipe.Steps.FirstOrDefault(s => s.StepNumber == _currentStepNumber);
            if (recipeStep != null)
            {
                AnalyzedSteps.Add(new ProductionStepDetail
                {
                    StepNumber = _currentStepNumber,
                    // YENİ: Adım adı artık PLC'den gelen anlık tipe göre belirleniyor
                    StepName = GetStepTypeName(status.AktifAdimTipiWordu),
                    TheoreticalTime = CalculateTheoreticalTime(recipeStep),
                    WorkingTime = "İşleniyor...",
                    StopTime = "00:00:00",
                    DeflectionTime = ""
                });
            }
        }

        private void FinalizeStep(int stepNumber)
        {
            var stepToFinalize = AnalyzedSteps.LastOrDefault(s => s.StepNumber == stepNumber && s.WorkingTime == "İşleniyor...");
            if (stepToFinalize == null) return;

            TimeSpan workingTime = DateTime.Now - CurrentStepStartTime;
            stepToFinalize.WorkingTime = workingTime.ToString(@"hh\:mm\:ss");

            if (_currentPauseStartTime.HasValue)
            {
                _currentStepPauseSeconds += (DateTime.Now - _currentPauseStartTime.Value).TotalSeconds;
                _currentPauseStartTime = null;
            }
            stepToFinalize.StopTime = TimeSpan.FromSeconds(_currentStepPauseSeconds).ToString(@"hh\:mm\:ss");

            TimeSpan theoreticalTime = TimeSpan.Parse(stepToFinalize.TheoreticalTime);
            TimeSpan actualWorkTime = workingTime - TimeSpan.FromSeconds(_currentStepPauseSeconds);
            TimeSpan deflection = actualWorkTime - theoreticalTime;

            string sign = deflection.TotalSeconds >= 0 ? "+" : "";
            stepToFinalize.DeflectionTime = $"{sign}{deflection:hh\\:mm\\:ss}";
        }

        private void HandleSkippedSteps(FullMachineStatus status, int fromStep, int toStep)
        {
            for (int i = fromStep; i < toStep; i++)
            {
                var recipeStep = _recipe.Steps.FirstOrDefault(s => s.StepNumber == i);
                if (recipeStep != null)
                {
                    string skippedStepName = GetStepTypeName(recipeStep.StepDataWords[24]) + " (Atlandı)";

                    AnalyzedSteps.Add(new ProductionStepDetail
                    {
                        StepNumber = i,
                        StepName = skippedStepName,
                        TheoreticalTime = CalculateTheoreticalTime(recipeStep),
                        WorkingTime = "00:00:00",
                        StopTime = "00:00:00",
                        DeflectionTime = ""
                    });
                }
            }
        }

        public ProductionStepDetail GetLastCompletedStep()
        {
            return AnalyzedSteps.LastOrDefault(s => s.WorkingTime != "İşleniyor...");
        }

        private string CalculateTheoreticalTime(ScadaRecipeStep step)
        {
            var parallelDurations = new List<double>();
            short controlWord = step.StepDataWords[24];
            if ((controlWord & 1) != 0) parallelDurations.Add(new SuAlmaParams(step.StepDataWords).MiktarLitre * SECONDS_PER_LITER);
            if ((controlWord & 8) != 0)
            {
                var dozajParams = new DozajParams(step.StepDataWords);
                double dozajSuresi = 0;
                if (dozajParams.AnaTankMakSu || dozajParams.AnaTankTemizSu) { dozajSuresi += 60; }
                dozajSuresi += dozajParams.CozmeSure;
                if (dozajParams.Tank1Dozaj) { dozajSuresi += dozajParams.DozajSure; }
                parallelDurations.Add(dozajSuresi);
            }
            if ((controlWord & 2) != 0) parallelDurations.Add(new IsitmaParams(step.StepDataWords).Sure * 60);
            if ((controlWord & 4) != 0) parallelDurations.Add(new CalismaParams(step.StepDataWords).CalismaSuresi * 60);
            if ((controlWord & 16) != 0) parallelDurations.Add(120); // Boşaltma sabit 120 sn
            if ((controlWord & 32) != 0) parallelDurations.Add(new SikmaParams(step.StepDataWords).SikmaSure * 60);
            double maxDurationSeconds = parallelDurations.Any() ? parallelDurations.Max() : 0;
            return TimeSpan.FromSeconds(maxDurationSeconds).ToString(@"hh\:mm\:ss");
        }

        // YENİ: Bu metot artık PLC'den gelen anlık kontrol word'ünü çözümlüyor.
        private string GetStepTypeName(short controlWord)
        {
            var stepTypes = new List<string>();
            if ((controlWord & 1) != 0) stepTypes.Add("Su Alma");
            if ((controlWord & 2) != 0) stepTypes.Add("Isıtma");
            if ((controlWord & 4) != 0) stepTypes.Add("Çalışma");
            if ((controlWord & 8) != 0) stepTypes.Add("Dozaj");
            if ((controlWord & 16) != 0) stepTypes.Add("Boşaltma");
            if ((controlWord & 32) != 0) stepTypes.Add("Sıkma");
            return stepTypes.Any() ? string.Join(" + ", stepTypes) : "Bekliyor...";
        }
    }
}