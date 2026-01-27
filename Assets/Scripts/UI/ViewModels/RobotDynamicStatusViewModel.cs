using Google.Protobuf;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.ViewModels
{
    public class RobotDynamicStatusViewModel : ProtoViewModelBase<RobotDynamicStatus>
    {
        private uint currentHealth;
        private float currentHeat;
        private float lastProjectileFireRate;
        private uint currentChassisEnergy;
        private uint currentBufferEnergy;
        private uint currentExperience;
        private uint experienceForUpgrade;
        private uint totalProjectilesFired;
        private uint remainingAmmo;
        private bool isOutOfCombat;
        private uint outOfCombatCountdown;
        private bool canRemoteHeal;
        private bool canRemoteAmmo;

        public uint CurrentHealth { get => currentHealth; set { if (currentHealth != value) { currentHealth = value; OnPropertyChanged(); } } }
        public float CurrentHeat { get => currentHeat; set { if (currentHeat != value) { currentHeat = value; OnPropertyChanged(); } } }
        public float LastProjectileFireRate { get => lastProjectileFireRate; set { if (lastProjectileFireRate != value) { lastProjectileFireRate = value; OnPropertyChanged(); } } }
        public uint CurrentChassisEnergy { get => currentChassisEnergy; set { if (currentChassisEnergy != value) { currentChassisEnergy = value; OnPropertyChanged(); } } }
        public uint CurrentBufferEnergy { get => currentBufferEnergy; set { if (currentBufferEnergy != value) { currentBufferEnergy = value; OnPropertyChanged(); } } }
        public uint CurrentExperience { get => currentExperience; set { if (currentExperience != value) { currentExperience = value; OnPropertyChanged(); } } }
        public uint ExperienceForUpgrade { get => experienceForUpgrade; set { if (experienceForUpgrade != value) { experienceForUpgrade = value; OnPropertyChanged(); } } }
        public uint TotalProjectilesFired { get => totalProjectilesFired; set { if (totalProjectilesFired != value) { totalProjectilesFired = value; OnPropertyChanged(); } } }
        public uint RemainingAmmo { get => remainingAmmo; set { if (remainingAmmo != value) { remainingAmmo = value; OnPropertyChanged(); } } }
        public bool IsOutOfCombat { get => isOutOfCombat; set { if (isOutOfCombat != value) { isOutOfCombat = value; OnPropertyChanged(); } } }
        public uint OutOfCombatCountdown { get => outOfCombatCountdown; set { if (outOfCombatCountdown != value) { outOfCombatCountdown = value; OnPropertyChanged(); } } }
        public bool CanRemoteHeal { get => canRemoteHeal; set { if (canRemoteHeal != value) { canRemoteHeal = value; OnPropertyChanged(); } } }
        public bool CanRemoteAmmo { get => canRemoteAmmo; set { if (canRemoteAmmo != value) { canRemoteAmmo = value; OnPropertyChanged(); } } }

        protected override void UpdateFrom(RobotDynamicStatus msg)
        {
            CurrentHealth = msg.CurrentHealth;
            CurrentHeat = msg.CurrentHeat;
            LastProjectileFireRate = msg.LastProjectileFireRate;
            CurrentChassisEnergy = msg.CurrentChassisEnergy;
            CurrentBufferEnergy = msg.CurrentBufferEnergy;
            CurrentExperience = msg.CurrentExperience;
            ExperienceForUpgrade = msg.ExperienceForUpgrade;
            TotalProjectilesFired = msg.TotalProjectilesFired;
            RemainingAmmo = msg.RemainingAmmo;
            IsOutOfCombat = msg.IsOutOfCombat;
            OutOfCombatCountdown = msg.OutOfCombatCountdown;
            CanRemoteHeal = msg.CanRemoteHeal;
            CanRemoteAmmo = msg.CanRemoteAmmo;
        }
    }
}
