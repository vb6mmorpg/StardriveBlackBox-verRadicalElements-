using System;

namespace Ship_Game
{
	public sealed class ModInformation
	{
		public string ModName = "";

		public string CustomMenuMusic = "";

		public string ModDescription = "";

		public bool DisableDefaultRaces;

		public string PortraitPath = "";

		public string ModImagePath_1920x1280 = "";

        public string URL = "";
        public string Author = "";

        //added by Gremlin
        public string Version;

        //added by McShooterz
        public bool useAlternateTech;
        public bool useHullBonuses;
        public bool useWeaponModifiers;
        public bool removeRemnantStory;
        public bool useCombatRepair;
        public bool clearVanillaTechs;

        //added by The Doctor
        public bool customMilTraitTechs;
        public bool customRemnantElements;
        public bool enableECM;
        public bool useDestroyers;
        public bool useDrones;
        public bool expandedWeaponCats;
        public bool overrideSecretsTree;
        public bool usePlanetaryProjection;
        public bool useProportionalUpkeep;
        public bool reconDropDown;
        public bool ColoniserMenu;
        public bool ConstructionModule;
        
        public float ShipyardBonus;
        public float UpkeepBaseline;
        public float UpkeepFighter;
        public float UpkeepCorvette;
        public float UpkeepFrigate;
        public float UpkeepCruiser;
        public float UpkeepCarrier;
        public float UpkeepCapital;
        public float UpkeepFreighter;
        public float UpkeepPlatform;
        public float UpkeepStation;
        public float UpkeepDrone;
        public float Spaceportscale = 0.8f;
        
        public int RemnantTechCount;


        // Doctor: Planet generation: % chance of each tile on this planet type being habitable. Default values as vanilla.
        public int BarrenHab = 0;
        public int IceHab = 15;
        public int OceanHab = 50;
        public int SteppeHab = 67;
        public int SwampHab = 67;
        public int TerranHab = 75;
        
        

		public ModInformation()
		{
		}
	}
}