﻿// UI/Views/PlcOperatorSettings_Control.cs
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TekstilScada.Models;
using TekstilScada.Repositories;
using TekstilScada.Services;

namespace TekstilScada.UI.Views
{
    public partial class PlcOperatorSettings_Control : UserControl
    {
        private MachineRepository _machineRepository;
        private PlcOperatorRepository _plcOperatorRepository;
        // DEĞİŞİKLİK: LsPlcManager -> IPlcManager
        private Dictionary<int, IPlcManager> _plcManagers;

        public PlcOperatorSettings_Control()
        {
            InitializeComponent();
            _plcOperatorRepository = new PlcOperatorRepository();
        }

        // DEĞİŞİKLİK: LsPlcManager -> IPlcManager
        public void InitializeControl(MachineRepository machineRepo, Dictionary<int, IPlcManager> plcManagers)
        {
            _machineRepository = machineRepo;
            _plcManagers = plcManagers;
        }

        private void PlcOperatorSettings_Control_Load(object sender, EventArgs e)
        {
            var machines = _machineRepository.GetAllEnabledMachines();
            cmbMachines.DataSource = machines;
            cmbMachines.DisplayMember = "DisplayInfo";
            cmbMachines.ValueMember = "Id";

            for (int i = 1; i <= 5; i++)
            {
                cmbSlot.Items.Add(i);
            }
            cmbSlot.SelectedIndex = 0;

            RefreshGrid();
        }

        private void RefreshGrid()
        {
            try
            {
                dgvOperators.DataSource = null;
                dgvOperators.DataSource = _plcOperatorRepository.GetAll();
                if (dgvOperators.Columns["SlotIndex"] != null) dgvOperators.Columns["SlotIndex"].HeaderText = "DB ID";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Operatör şablonları yüklenirken hata: {ex.Message}", "Hata");
            }
        }

        private void btnRead_Click(object sender, EventArgs e)
        {
            if (cmbMachines.SelectedItem is Machine selectedMachine)
            {
                if (_plcManagers.TryGetValue(selectedMachine.Id, out var plcManager))
                {
                    int slotIndex = cmbSlot.SelectedIndex; // 0-4
                    ReadOperatorFromPlc(plcManager, slotIndex);
                }
            }
        }

        // DEĞİŞİKLİK: LsPlcManager -> IPlcManager
        private async void ReadOperatorFromPlc(IPlcManager plcManager, int slotIndex)
        {
            this.Cursor = Cursors.WaitCursor;
            var result = await plcManager.ReadSinglePlcOperatorAsync(slotIndex);
            this.Cursor = Cursors.Default;

            if (result.IsSuccess)
            {
                var opFromPlc = result.Content;
                _plcOperatorRepository.SaveOrUpdate(opFromPlc);
                RefreshGrid();
                MessageBox.Show($"Makinedeki {slotIndex + 1}. sıradaki operatör bilgisi okundu ve listeye eklendi/güncellendi.", "Başarılı");
            }
            else
            {
                MessageBox.Show($"Operatör okunurken hata: {result.Message}", "Hata");
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (dgvOperators.SelectedRows.Count == 0)
            {
                MessageBox.Show("Lütfen PLC'ye göndermek için listeden bir operatör seçin.", "Uyarı");
                return;
            }
            if (cmbMachines.SelectedItem is Machine selectedMachine)
            {
                if (_plcManagers.TryGetValue(selectedMachine.Id, out var plcManager))
                {
                    var selectedOperator = dgvOperators.SelectedRows[0].DataBoundItem as PlcOperator;
                    int slotIndex = cmbSlot.SelectedIndex; // 0-4
                    selectedOperator.SlotIndex = slotIndex;

                    SendOperatorToPlc(plcManager, selectedOperator);
                }
            }
        }

        // DEĞİŞİKLİK: LsPlcManager -> IPlcManager
        private async void SendOperatorToPlc(IPlcManager plcManager, PlcOperator plcOperator)
        {
            this.Cursor = Cursors.WaitCursor;
            var result = await plcManager.WritePlcOperatorAsync(plcOperator);
            this.Cursor = Cursors.Default;

            if (result.IsSuccess)
            {
                MessageBox.Show($"'{plcOperator.Name}' operatörü, seçilen makinenin {plcOperator.SlotIndex + 1}. sırasına başarıyla yazıldı.", "Başarılı");
            }
            else
            {
                MessageBox.Show($"Operatör gönderilirken hata: {result.Message}", "Hata");
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (dgvOperators.SelectedRows.Count > 0)
            {
                var selectedOperator = dgvOperators.SelectedRows[0].DataBoundItem as PlcOperator;
                var result = MessageBox.Show($"'{selectedOperator.Name}' şablonunu silmek istediğinizden emin misiniz?", "Onay", MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    _plcOperatorRepository.Delete(selectedOperator.SlotIndex);
                    RefreshGrid();
                }
            }
        }
    }
}
