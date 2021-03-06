using Microsoft.Xna.Framework;
using Ship_Game;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ship_Game.Gameplay
{
	public sealed class ArtificialIntelligence : IDisposable
	{
       // public Task fireTask;
        public bool UseSensorsForTargets = true;
        public bool ClearOrdersNext;

		private Vector2 aiNewDir;

		//private int aiNumSeen;

		public static UniverseScreen universeScreen; //used for?

		public Ship Owner; //used for? indicates owner of this ship?

		public GameplayObject Target;

		public AIState State = AIState.AwaitingOrders;

		public Ship_Game.Gameplay.CombatState CombatState = Ship_Game.Gameplay.CombatState.AttackRuns;

		public Guid OrbitTargetGuid;

		public Ship_Game.CombatAI CombatAI = new Ship_Game.CombatAI();
        //public Ship_Game.CombatAI CombatAI = new Ship_Game.CombatAI(This);

		public BatchRemovalCollection<ArtificialIntelligence.ShipWeight> NearbyShips = new BatchRemovalCollection<ArtificialIntelligence.ShipWeight>();

		public List<Ship> PotentialTargets = new List<Ship>();


		private Vector2 direction = Vector2.Zero;

		private int resupplystep;

		public Planet resupplyTarget;

		public Planet start;

		public Planet end;

		private SolarSystem SystemToPatrol;

		private List<Planet> PatrolRoute = new List<Planet>();

		private int stopNumber;

		private Planet PatrolTarget;

		public SolarSystem SystemToDefend;

		public Guid SystemToDefendGuid;

		private List<SolarSystem> SystemsToExplore = new List<SolarSystem>();

		public SolarSystem ExplorationTarget;

		public Ship EscortTarget;

		public Guid EscortTargetGuid;

		private List<float> Distances = new List<float>();

		private float findNewPosTimer;

		private Goal ColonizeGoal;

		private Planet awaitClosest;

		public bool inOrbit;

		private Vector2 OrbitPos;

		private float DistanceLast;

		public bool HasPriorityOrder; //used to determine if there exists a "PriorityOrder" in the OrderQueue

		private Vector2 negativeRotation = Vector2.One;

		public int GotoStep;

		private bool AttackRunStarted;

		private float AttackRunAngle;

		private float runTimer;

		private Vector2 AttackVector = Vector2.Zero;

		public AIState DefaultAIState = AIState.AwaitingOrders;

		private FleetDataNode node;

		public bool HadPO;

		private float ScanForThreatTimer;

		public Vector2 MovePosition;

		private float DesiredFacing;

		private Vector2 FinalFacingVector;

		public LinkedList<ArtificialIntelligence.ShipGoal> OrderQueue = new LinkedList<ArtificialIntelligence.ShipGoal>();

		public Queue<Vector2> ActiveWayPoints = new Queue<Vector2>();

		public Planet ExterminationTarget;

		public string FoodOrProd;

		private float moveTimer;

		public bool hasPriorityTarget;

		public bool Intercepting;

		public List<Ship> TargetQueue = new List<Ship>();

		private float TriggerDelay = 0;

		public Guid TargetGuid;

		public Guid ColonizeTargetGuid;

		public Planet ColonizeTarget;

		public bool ReadyToWarp = true;

		public Planet OrbitTarget;

		private float OrbitalAngle = RandomMath.RandomBetween(0f, 360f);

		public bool IgnoreCombat;

		public BatchRemovalCollection<Ship> FriendliesNearby = new BatchRemovalCollection<Ship>();

		public bool BadGuysNear;
        //added by gremlin: new troopsout property. Change this to use actual troopsout 
        public bool troopsout = false;

        private float UtilityModuleCheckTimer;
        public object wayPointLocker;
        public Ship TargetShip;
        public  ReaderWriterLockSlim orderqueue = new ReaderWriterLockSlim();
        //public  List<Task> TaskList = new List<Task>();
        //public Dictionary<Weapon, GameplayObject> visible = new Dictionary<Weapon, GameplayObject>();
       // private GameplayObject secondarytarget = null;
        //private GameplayObject pdtarget = null;
        //private float targetChangeTimer =0;

        //adding for thread safe Dispose because class uses unmanaged resources 
        private bool disposed;
        public List<Projectile> TrackProjectiles = new List<Projectile>();

		public ArtificialIntelligence()
		{
		}

		public ArtificialIntelligence(Ship owner)
		{
			this.Owner = owner;
			this.State = AIState.AwaitingOrders;
            
            this.wayPointLocker = new Object();
		}

		private void aPlotCourseToNew(Vector2 endPos, Vector2 startPos)
		{
			float Distance = Vector2.Distance(startPos, endPos);
			if (Distance < this.Owner.CalculateRange())
			{
                lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Enqueue(endPos);
				}
				return;
			}
			bool startInBorders = false;
			bool endInBorders = false;
            this.Owner.loyalty.BorderNodeLocker.EnterReadLock();
			{
				foreach (Empire.InfluenceNode node in this.Owner.loyalty.BorderNodes)
				{
					if (Vector2.Distance(node.Position, startPos) <= node.Radius)
					{
						startInBorders = true;
					}
					if (Vector2.Distance(node.Position, endPos) > node.Radius)
					{
						continue;
					}
					endInBorders = true;
				}
			}
            this.Owner.loyalty.BorderNodeLocker.ExitReadLock();
			if (startInBorders && endInBorders)
			{
				bool AllTravelIsInBorders = true;
				float angle = HelperFunctions.findAngleToTarget(startPos, endPos);
				int numChecks = (int)Distance / 2500;
				for (int i = 0; i < numChecks; i++)
				{
					bool goodPoint = false;
					Vector2 pointToCheck = HelperFunctions.findPointFromAngleAndDistance(startPos, angle, (float)(2500 * i));
                    this.Owner.loyalty.BorderNodeLocker.EnterReadLock();
					{
						foreach (Empire.InfluenceNode node in this.Owner.loyalty.BorderNodes)
						{
							if (Vector2.Distance(node.Position, pointToCheck) > (node.Radius * 0.85))
							{
								continue;
							}
							goodPoint = true;
							break;
						}
						if (!goodPoint)
						{
							AllTravelIsInBorders = false;
						}
					}
                    this.Owner.loyalty.BorderNodeLocker.ExitReadLock();
				}
				if (AllTravelIsInBorders)
				{
					lock (this.wayPointLocker)
					{
						this.ActiveWayPoints.Enqueue(endPos);
					}
					return;
				}
			}
			IOrderedEnumerable<Ship> sortedList = 
				from ship in this.Owner.loyalty.GetShips()
				orderby this.Owner.CalculateRange() - Vector2.Distance(startPos, ship.Center)
				select ship;
			bool aCloserNodeExists = false;
			foreach (Ship ship1 in sortedList)
			{
				if (this.Owner.CalculateRange() - Vector2.Distance(startPos, ship1.Center) < 0f || !(ship1.Name == "Subspace Projector"))
				{
					continue;
				}
				float DistanceFromProjectorToFinalSpot = Vector2.Distance(ship1.Center, endPos);
				Vector2.Distance(this.Owner.Center, ship1.Center);
				if (DistanceFromProjectorToFinalSpot >= Distance)
				{
					continue;
				}
				aCloserNodeExists = true;
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Enqueue(ship1.Center);
				}
				this.PlotCourseToNew(endPos, ship1.Center);
				break;
			}
			if (!aCloserNodeExists)
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Enqueue(endPos);
				}
			}
		}

		private void AwaitOrders(float elapsedTime)
		{
            //if ((this.Owner.GetSystem() ==null && this.State == AIState.Intercept) 
            //    || this.Target != null && this.Owner.GetSystem()!=null && this.Target.GetSystem()!=null && this.Target.GetSystem()==this.Owner.GetSystem())
            //    return;
            //if (this.Owner.InCombatTimer > elapsedTime * -5 && ScanForThreatTimer < 2 - elapsedTime * 5)
            //    this.ScanForThreatTimer = 0;
            this.HasPriorityOrder = false;
			if (this.awaitClosest != null)
			{
				this.DoOrbit(this.awaitClosest, elapsedTime);
			}
			else if (this.Owner.GetSystem() == null)
			{
				IOrderedEnumerable<SolarSystem> sortedList = 
					from solarsystem in this.Owner.loyalty.GetOwnedSystems()
					orderby Vector2.Distance(this.Owner.Center, solarsystem.Position)
					select solarsystem;
				if (sortedList.Count<SolarSystem>() > 0)
				{
					this.DoOrbit(sortedList.First<SolarSystem>().PlanetList[0], elapsedTime);
					this.awaitClosest = sortedList.First<SolarSystem>().PlanetList[0];
					return;
				}
			}
			else
			{
				float closestD = 999999f;
				foreach (Planet p in this.Owner.GetSystem().PlanetList)
				{
					float Distance = Vector2.Distance(this.Owner.Center, p.Position);
					if (Distance >= closestD)
					{
						continue;
					}
					closestD = Distance;
					this.awaitClosest = p;
				}
                //if (this.State == AIState.SystemDefender)
                //    this.State = AIState.AwaitingOrders;
			}
		}

		private void AwaitOrdersPlayer(float elapsedTime)
		{
			this.HasPriorityOrder = false;
            if (this.Owner.InCombatTimer > elapsedTime * -5 && ScanForThreatTimer < 2 - elapsedTime * 5)
                this.ScanForThreatTimer = 0;
            if (this.awaitClosest != null)
			{
				this.DoOrbit(this.awaitClosest, elapsedTime);
				return;
			}
			List<Planet> planets = new List<Planet>();
			foreach (KeyValuePair<Guid, Planet> entry in ArtificialIntelligence.universeScreen.PlanetsDict)
			{
				planets.Add(entry.Value);
			}
			IOrderedEnumerable<Planet> sortedList = 
				from planet in planets
				orderby Vector2.Distance(planet.Position, this.Owner.Center)
				select planet;
			this.awaitClosest = sortedList.First<Planet>();
		}

		private void Colonize(Planet TargetPlanet)
		{
			if (Vector2.Distance(this.Owner.Center, TargetPlanet.Position) > 2000f)
			{
				this.OrderQueue.RemoveFirst();
				this.OrderColonization(TargetPlanet);
				this.State = AIState.Colonize;
				return;
			}
			if (TargetPlanet.Owner != null || !TargetPlanet.habitable)
			{
				if (this.ColonizeGoal != null)
				{
					Goal colonizeGoal = this.ColonizeGoal;
					colonizeGoal.Step = colonizeGoal.Step + 1;
					this.Owner.loyalty.GetGSAI().Goals.QueuePendingRemoval(this.ColonizeGoal);
				}
				this.State = AIState.AwaitingOrders;
				this.OrderQueue.Clear();
				return;
			}
			this.ColonizeTarget = TargetPlanet;
			this.ColonizeTarget.Owner = this.Owner.loyalty;
			this.ColonizeTarget.system.OwnerList.Add(this.Owner.loyalty);
			this.ColonizeTarget.colonyType = this.Owner.loyalty.AssessColonyNeeds(this.ColonizeTarget);
			if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				ArtificialIntelligence.universeScreen.NotificationManager.AddColonizedNotification(this.ColonizeTarget, EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty));
				this.ColonizeTarget.colonyType = Planet.ColonyType.Colony;
			}
			//lock (GlobalStats.OwnedPlanetsLock)
			{
				this.Owner.loyalty.AddPlanet(this.ColonizeTarget);
			}
			this.ColonizeTarget.InitializeSliders(this.Owner.loyalty);
			this.ColonizeTarget.ExploredDict[this.Owner.loyalty] = true;
			List<string> BuildingsAdded = new List<string>();
			foreach (ModuleSlot slot in this.Owner.ModuleSlotList)
			{
				if (slot.module == null || slot.module.ModuleType != ShipModuleType.Colony || slot.module.DeployBuildingOnColonize == null || BuildingsAdded.Contains(slot.module.DeployBuildingOnColonize))
				{
					continue;
				}
				Building building = ResourceManager.GetBuilding(slot.module.DeployBuildingOnColonize);
				bool ok = true;
				if (building.Unique)
				{
					foreach (Building b in this.ColonizeTarget.BuildingList)
					{
						if (b.Name != building.Name)
						{
							continue;
						}
						ok = false;
						break;
					}
				}
				if (!ok)
				{
					continue;
				}
				BuildingsAdded.Add(slot.module.DeployBuildingOnColonize);
				this.ColonizeTarget.BuildingList.Add(building);
				this.ColonizeTarget.AssignBuildingToTileOnColonize(building);
			}
			Planet colonizeTarget = this.ColonizeTarget;
			colonizeTarget.TerraformPoints = colonizeTarget.TerraformPoints + this.Owner.loyalty.data.EmpireFertilityBonus;
			this.ColonizeTarget.Crippled_Turns = 0;
			if (StatTracker.SnapshotsDict.ContainsKey(ArtificialIntelligence.universeScreen.StarDate.ToString("#.0")))
			{
				StatTracker.SnapshotsDict[ArtificialIntelligence.universeScreen.StarDate.ToString("#.0")][EmpireManager.EmpireList.IndexOf(this.Owner.loyalty)].Events.Add(string.Concat(this.Owner.loyalty.data.Traits.Name, " colonized ", this.ColonizeTarget.Name));
				NRO nro = new NRO()
				{
					Node = this.ColonizeTarget.Position,
					Radius = 300000f,
					StarDateMade = ArtificialIntelligence.universeScreen.StarDate
				};
				StatTracker.SnapshotsDict[ArtificialIntelligence.universeScreen.StarDate.ToString("#.0")][EmpireManager.EmpireList.IndexOf(this.Owner.loyalty)].EmpireNodes.Add(nro);
			}
			foreach (Goal g in this.Owner.loyalty.GetGSAI().Goals)
			{
				if (g.type != GoalType.Colonize || g.GetMarkedPlanet() != this.ColonizeTarget)
				{
					continue;
				}
				this.Owner.loyalty.GetGSAI().Goals.QueuePendingRemoval(g);
				break;
			}
			this.Owner.loyalty.GetGSAI().Goals.ApplyPendingRemovals();
			if (this.ColonizeTarget.system.OwnerList.Count > 1)
			{
				foreach (Planet p in this.ColonizeTarget.system.PlanetList)
				{
					if (p.Owner == this.ColonizeTarget.Owner || p.Owner == null)
					{
						continue;
					}
                    this.Owner.loyalty.GetPlanets().thisLock.EnterReadLock();
					{
						if (p.Owner.GetRelations().ContainsKey(this.Owner.loyalty) && !p.Owner.GetRelations()[this.Owner.loyalty].Treaty_OpenBorders)
						{
							p.Owner.DamageRelationship(this.Owner.loyalty, "Colonized Owned System", 20f, p);
						}
					}
                    this.Owner.loyalty.GetPlanets().thisLock.ExitReadLock();
				}
			}
			foreach (ModuleSlot slot in this.Owner.ModuleSlotList)
			{
				if (slot.module.ModuleType != ShipModuleType.Colony)
				{
					continue;
				}
				Planet foodHere = this.ColonizeTarget;
				foodHere.FoodHere = foodHere.FoodHere + slot.module.numberOfFood;
				Planet productionHere = this.ColonizeTarget;
				productionHere.ProductionHere = productionHere.ProductionHere + slot.module.numberOfEquipment;
				Planet population = this.ColonizeTarget;
				population.Population = population.Population + slot.module.numberOfColonists;
			}
            //Added by McShooterz: Remove troops from planet
            bool TroopsRemoved = false;
            bool PlayerTroopsRemoved = false;
            for (int i = 0; i < this.ColonizeTarget.TroopsHere.Count; i++)
            {
                Troop troop = this.ColonizeTarget.TroopsHere[i];
                if (troop != null && troop.GetOwner() != null && !troop.GetOwner().isFaction && troop.GetOwner() != this.ColonizeTarget.Owner && this.ColonizeTarget.Owner.GetRelations().ContainsKey(troop.GetOwner()) && !this.ColonizeTarget.Owner.GetRelations()[troop.GetOwner()].AtWar)
                {
                    troop.Launch();
                    TroopsRemoved = true;
                    if (troop.GetOwner().isPlayer)
                        PlayerTroopsRemoved = true;
                }
            }
            if (TroopsRemoved)
            {
                if (PlayerTroopsRemoved)
                    universeScreen.NotificationManager.AddTroopsRemovedNotification(this.ColonizeTarget);
                else if (this.ColonizeTarget.Owner.isPlayer)
                    universeScreen.NotificationManager.AddForeignTroopsRemovedNotification(this.ColonizeTarget);
            }
			this.Owner.QueueTotalRemoval();
		}

		private void DeRotate()
		{
			if (this.Owner.yRotation > 0f)
			{
				Ship owner = this.Owner;
				owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
				if (this.Owner.yRotation < 0f)
				{
					this.Owner.yRotation = 0f;
					return;
				}
			}
			else if (this.Owner.yRotation < 0f)
			{
				Ship ship = this.Owner;
				ship.yRotation = ship.yRotation + this.Owner.yBankAmount;
				if (this.Owner.yRotation > 0f)
				{
					this.Owner.yRotation = 0f;
				}
			}
		}

		private void DoAssaultShipCombat(float elapsedTime)
		{
			this.DoNonFleetArtillery(elapsedTime);
			float OurTroopStrength = 0f;
			float EnemyStrength = 0f;
			if (this.Target != null && this.Target is Ship)
			{
				EnemyStrength = (this.Target as Ship).MechanicalBoardingDefense + (this.Target as Ship).TroopBoardingDefense;
			}
			foreach (Troop t in this.Owner.TroopList)
			{
				OurTroopStrength = OurTroopStrength + (float)t.Strength;
			}
			if (OurTroopStrength / 2f > EnemyStrength && (this.Target as Ship).GetStrength() > 0f && this.Owner.GetHangars().Count > 0)
			{
				this.Owner.ScrambleAssaultShips();
				foreach (ShipModule hangar in this.Owner.GetHangars())
				{
					if (hangar.GetHangarShip() == null || this.Target == null || !(hangar.GetHangarShip().Role == "troop") || !((this.Target as Ship).Role == "frigate") && !((this.Target as Ship).Role == "carrier") && !((this.Target as Ship).Role == "corvette") && !((this.Target as Ship).Role == "cruiser") && !((this.Target as Ship).Role == "capital"))
					{
						continue;
					}
					hangar.GetHangarShip().GetAI().OrderTroopToBoardShip(this.Target as Ship);
				}
			}
		}

		private void DoAttackRunOrig(float elapsedTime)
		{
			float distanceToTarget = Vector2.Distance(this.Owner.Center, this.Target.Center);
			if (distanceToTarget > this.Owner.Radius * 3f + this.Target.Radius && distanceToTarget > this.Owner.maxWeaponsRange / 2f)
			{
				this.runTimer = 0f;
				this.AttackRunStarted = false;
				this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
				return;
			}
			if (distanceToTarget < this.Owner.maxWeaponsRange)
			{
				ArtificialIntelligence artificialIntelligence = this;
				artificialIntelligence.runTimer = artificialIntelligence.runTimer + elapsedTime;
				if (this.runTimer >= 7f)
				{
					this.DoNonFleetArtillery(elapsedTime);
					return;
				}
				Vector2 projectedPosition = this.Target.Center;
				ArtificialIntelligence target = this;
				target.aiNewDir = target.aiNewDir + (this.findVectorToTarget(this.Owner.Center, projectedPosition) * 0.35f);
				if (distanceToTarget < (this.Owner.Radius + this.Target.Radius) * 3f && !this.AttackRunStarted)
				{
					this.AttackRunStarted = true;
					int ran = (int)((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(1f, 100f);
					ran = (ran <= 50 ? 1 : -1);
					this.AttackRunAngle = (float)ran * ((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(75f, 100f) + MathHelper.ToDegrees(this.Owner.Rotation);
					this.AttackVector = this.findPointFromAngleAndDistance(this.Owner.Center, this.AttackRunAngle, 1500f);
				}
				this.AttackVector = this.findPointFromAngleAndDistance(this.Owner.Center, this.AttackRunAngle, 1500f);
				this.MoveInDirection(this.AttackVector, elapsedTime);
			}
		}
        //aded by gremlin Deveksmod Attackrun
        private void DoAttackRun(float elapsedTime)
        {

            float distanceToTarget = Vector2.Distance(this.Owner.Center, this.Target.Center);
            float spacerdistance = this.Owner.Radius * 3 + this.Target.Radius;
            if (spacerdistance > this.Owner.maxWeaponsRange * .35f)
                spacerdistance = this.Owner.maxWeaponsRange * .35f;


            if (distanceToTarget > spacerdistance && distanceToTarget > this.Owner.maxWeaponsRange * .35f)
            {
                this.runTimer = 0f;
                this.AttackRunStarted = false;
                this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
                return;
            }


            if (distanceToTarget < this.Owner.maxWeaponsRange * .35f)// *.35f)
            {
                ArtificialIntelligence artificialIntelligence = this;
                artificialIntelligence.runTimer = artificialIntelligence.runTimer + elapsedTime;
                if ((double)this.runTimer > 7f) //this.Owner.Weapons.Average(delay => delay.timeToNextFire)) //7 * (this.Owner.maxWeaponsRange + 1) / (this.Owner.GetSTLSpeed()+ 1))
                {
                    this.DoNonFleetArtillery(elapsedTime);
                    return;

                }
                //if (!AttackRunStarted )
                //{
                //    this.Stop(elapsedTime);
                //    //return;
                //}
                Vector2 projectedPosition = this.Target.Center + this.Target.Velocity;
                ArtificialIntelligence target = this;
                target.aiNewDir = target.aiNewDir + (this.findVectorToTarget(this.Owner.Center, projectedPosition) * 0.35f);
                if (distanceToTarget < (this.Owner.Radius + this.Target.Radius) * 3f && !this.AttackRunStarted)
                {
                    this.AttackRunStarted = true;
                    int ran = (int)((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(1f, 100f);
                    ran = (ran <= 50 ? 1 : -1);
                    this.AttackRunAngle = (float)ran * ((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(75f, 100f) + MathHelper.ToDegrees(this.Owner.Rotation);
                    this.AttackVector = this.findPointFromAngleAndDistance(this.Owner.Center, this.AttackRunAngle, 1500f);
                }
                this.AttackVector = this.findPointFromAngleAndDistance(this.Owner.Center, this.AttackRunAngle, 1500f);
                this.MoveInDirection(this.AttackVector, elapsedTime);
                if (this.runTimer > 2)
                {
                    this.DoNonFleetArtillery(elapsedTime);
                    return;
                }

            }
            //else
            //{
            //    this.DoNonFleetArtillery(elapsedTime);
            //}


        }
		private void DoBoardShip(float elapsedTime)
		{
			this.hasPriorityTarget = true;
			this.State = AIState.Boarding;
			if (this.EscortTarget == null || !this.EscortTarget.Active)
			{
				this.OrderQueue.Clear();
				return;
			}
			if (this.EscortTarget.loyalty == this.Owner.loyalty)
			{
				this.OrderReturnToHangar();
				return;
			}
			this.ThrustTowardsPosition(this.EscortTarget.Center, elapsedTime, this.Owner.speed);
			float Distance = Vector2.Distance(this.Owner.Center, this.EscortTarget.Center);
			//added by gremlin distance at which troops can board enemy ships
            if (Distance < this.EscortTarget.Radius + 300f)
			{
				if (this.Owner.TroopList.Count > 0)
				{
					this.EscortTarget.TroopList.Add(this.Owner.TroopList[0]);
					this.Owner.QueueTotalRemoval();
					return;
				}
			}
			else if (Distance > 10000f && this.Owner.Mothership != null && this.Owner.Mothership.GetAI().CombatState == Ship_Game.Gameplay.CombatState.AssaultShip)
			{
				this.OrderReturnToHangar();
			}
		}

        private void DoCombat(float elapsedTime)
        {
            this.awaitClosest = null;
            this.State = AIState.Combat;
            this.Owner.InCombat = true;
            this.Owner.InCombatTimer = 15f;
            #region modified 07/28/15
            float radius = 30000f;                      //Added by Ermenildo V. Castro, Jr.; 07/28/15
            Vector2 senseCenter = this.Owner.Center;    //Added by Ermenildo V. Castro, Jr.; 07/28/15

            //If there is a target, and the target is inactive
            //remove inactive target from consideration
            if (this.Target != null && !this.Target.Active)
            {
                this.Target = null;
                this.Intercepting = false;
                if (!this.BadGuysNear) //Modified by Ermenildo V. Castro, Jr; 07/28/15: re-instated commented out conditional
                {
                    this.State = this.DefaultAIState;
                    this.OrderQueue.Clear();
                    this.Target = this.ScanForCombatTargets(senseCenter, radius); //Modified by Modified by Ermenildo V. Castro, Jr; 07/28/15; //this.Target = null;
                    if (Target != null) { this.PotentialTargets.Add((Ship)Target); }
                }
                return;
                
            }
            //if no target available
            if (this.Target == null )
            {
                this.Target = null;
                this.Intercepting = false;
                this.ScanForThreatTimer = 0;
                if (!this.BadGuysNear) //Modified by Ermenildo V. Castro, Jr; 07/28/15: re-instated commented out conditional
                {
                    this.OrderQueue.Clear();
                    this.State = this.DefaultAIState;
                    this.Target = this.ScanForCombatTargets(senseCenter, radius); //Modified by Modified by Ermenildo V. Castro, Jr; 07/28/15; //this.Target = null;
                    if (Target != null) { this.PotentialTargets.Add((Ship)Target); }

                }
                return;
                
               
            }
            #endregion

            if (this.Owner.Mothership != null && this.Owner.Mothership.Active)
            {

                //if (!this.hasPriorityTarget
                //    && !this.HasPriorityOrder&& this.Target != null 
                //    && this.Owner.Mothership.GetAI().Target == null 
                //    && !this.Owner.Mothership.GetAI().HasPriorityOrder 
                //    && !this.Owner.Mothership.GetAI().hasPriorityTarget)
                //{
                //    this.Owner.Mothership.GetAI().Target = this.Target;
                //    this.Owner.Mothership.GetAI().State = AIState.Combat;
                //    this.Owner.Mothership.InCombatTimer = 15f;
                //}
                if(this.Owner.Role != "troop"
                    && (this.Owner.Health/ this.Owner.HealthMax <.5f || (this.Owner.shield_max>0 && this.Owner.shield_percent <=0.0))
                    || (this.Owner.OrdinanceMax > 0 && this.Owner.Ordinance / this.Owner.OrdinanceMax <= .1f)
                    || (this.Owner.PowerCurrent <=1f && this.Owner.PowerDraw / this.Owner.PowerFlowMax <=.1f)
                    )
                    this.OrderReturnToHangar();
            }
            if (this.Owner.OrdinanceMax > 0f && this.Owner.Ordinance / this.Owner.OrdinanceMax < 0.05f &&  !this.hasPriorityTarget)//this.Owner.loyalty != ArtificialIntelligence.universeScreen.player)
            {
                if (FriendliesNearby.Where(supply => supply.HasSupplyBays && supply.Ordinance >= 100).Count() == 0)
                {
                    this.OrderResupplyNearest();
                    return;
                }
            }
            if(this.Owner.Level >2 && this.Owner.Health / this.Owner.HealthMax <.5f&&  !(this.HasPriorityOrder||this.hasPriorityTarget))
            {
                                 this.OrderResupplyNearest();
                    return;
            }
            if (Vector2.Distance(this.Target.Center, this.Owner.Center) < 10000f)
            {
                if (this.Owner.engineState != Ship.MoveState.Warp && this.Owner.GetHangars().Count > 0 && !this.Owner.ManualHangarOverride)
                {
                    if (!this.Owner.FightersOut) this.Owner.FightersOut = true;
                }
                if (this.Owner.engineState == Ship.MoveState.Warp)
                {
                    this.Owner.HyperspaceReturn();
                }

            }
            else if (this.CombatState != CombatState.HoldPosition && this.CombatState != CombatState.Evade)
            {
                this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
                return;
            }
            if (!this.HasPriorityOrder && !this.hasPriorityTarget && this.Owner.Weapons.Count == 0 && this.Owner.GetHangars().Count == 0)
            {
                this.CombatState = CombatState.Evade;
            }
            if (!this.Owner.loyalty.isFaction && this.Owner.GetSystem() != null && this.TroopsOut == false && this.Owner.GetHangars().Where(troops => troops.IsTroopBay).Count() > 0 || this.Owner.hasTransporter)
            {
                if (this.Owner.TroopList.Where(troop => troop.GetOwner() == this.Owner.loyalty).Count() > 0 && this.Owner.TroopList.Where(troop => troop.GetOwner() != this.Owner.loyalty).Count() == 0)
                {
                    Planet invadeThis = null;
                    foreach (Planet invade in this.Owner.GetSystem().PlanetList.Where(owner => owner.Owner != null && owner.Owner != this.Owner.loyalty).OrderBy(troops => troops.TroopsHere.Count))
                    {
                        if (this.Owner.loyalty.GetRelations()[invade.Owner].AtWar)
                        {
                            invadeThis = invade;
                            break;
                        }
                    }
                    if (!this.TroopsOut && !this.Owner.hasTransporter)
                    {
                        if (invadeThis != null)
                        {
                            this.TroopsOut = true;
                            foreach (Ship troop in this.Owner.GetHangars().Where(troop => troop.IsTroopBay && troop.GetHangarShip() != null).Select(ship => ship.GetHangarShip()))
                            {
                                troop.GetAI().OrderAssaultPlanet(invadeThis);
                            }
                        }
                        else if (this.Target != null && this.Target is Ship && (this.Target as Ship).Role == "frigate" || (this.Target as Ship).Role == "carrier" || (this.Target as Ship).Role == "corvette" || (this.Target as Ship).Role == "cruiser" || (this.Target as Ship).Role == "capital")
                        {
                            if (this.Owner.GetHangars().Where(troop => troop.IsTroopBay).Count() * 60 >= (this.Target as Ship).MechanicalBoardingDefense)
                            {
                                this.TroopsOut = true;
                                foreach (ShipModule hangar in this.Owner.GetHangars())
                                {
                                    if (hangar.GetHangarShip() == null || this.Target == null || !(hangar.GetHangarShip().Role == "troop") || !((this.Target as Ship).Role == "frigate") && !((this.Target as Ship).Role == "carrier") && !((this.Target as Ship).Role == "corvette") && !((this.Target as Ship).Role == "cruiser") && !((this.Target as Ship).Role == "capital"))
                                    {
                                        continue;
                                    }
                                    hangar.GetHangarShip().GetAI().OrderTroopToBoardShip(this.Target as Ship);
                                }
                            }
                        }
                        else
                        {
                            this.TroopsOut = false;
                        }
                    }

                }
            }
            //if this ship does not belong to a fleet
            #region
            if (this.Owner.fleet == null)
            {
                switch (this.CombatState)
                {
                    case CombatState.Artillery:
                        {
                            this.DoNonFleetArtillery(elapsedTime);
                            break;
                        }
                    case CombatState.OrbitLeft:
                        {
                            this.OrbitShipLeft(this.Target as Ship, elapsedTime);
                            break;
                        }
                    case CombatState.BroadsideLeft:
                        {
                            this.DoNonFleetBroadsideLeft(elapsedTime);
                            break;
                        }
                    case CombatState.OrbitRight:
                        {
                            this.OrbitShip(this.Target as Ship, elapsedTime);
                            break;
                        }
                    case CombatState.BroadsideRight:
                        {
                            this.DoNonFleetBroadsideRight(elapsedTime);
                            break;
                        }
                    case CombatState.AttackRuns:
                        {
                            this.DoAttackRun(elapsedTime);
                            break;
                        }
                    case CombatState.HoldPosition:
                        {
                            this.DoHoldPositionCombat(elapsedTime);
                            break;
                        }
                    case CombatState.Evade:
                        {
                            this.DoEvadeCombat(elapsedTime);
                            break;
                        }
                    case CombatState.AssaultShip:
                        {
                            this.DoAssaultShipCombat(elapsedTime);
                            break;
                        }
                }
            }
            #endregion
            //if ship belongs to a fleet
            #region
            else if (this.Owner.fleet != null)
            {
                switch (this.CombatState)
                {
                    case CombatState.Artillery:
                        {
                            this.DoNonFleetArtillery(elapsedTime);
                            break;
                        }
                    case CombatState.OrbitLeft:
                        {
                            this.OrbitShipLeft(this.Target as Ship, elapsedTime);
                            break;
                        }
                    case CombatState.BroadsideLeft:
                        {
                            this.DoNonFleetBroadsideLeft(elapsedTime);
                            break;
                        }
                    case CombatState.OrbitRight:
                        {
                            this.OrbitShip(this.Target as Ship, elapsedTime);
                            break;
                        }
                    case CombatState.BroadsideRight:
                        {
                            this.DoNonFleetBroadsideRight(elapsedTime);
                            break;
                        }
                    case CombatState.AttackRuns:
                        {
                            this.DoAttackRun(elapsedTime);
                            break;
                        }
                    case CombatState.HoldPosition:
                        {
                            this.DoHoldPositionCombat(elapsedTime);
                            break;
                        }
                    case CombatState.Evade:
                        {
                            this.DoEvadeCombat(elapsedTime);
                            break;
                        }
                    case CombatState.AssaultShip:
                        {
                            this.DoAssaultShipCombat(elapsedTime);
                            break;
                        }
                }
                if (this.Target != null)
                    return;
                this.Owner.InCombat = false;
            }
            #endregion
        }

        //added by gremlin : troops out property        
        public bool TroopsOut
        {
            get
            {
                //this.troopsout = false;
                if (this.Owner.TroopsOut)
                {
                    this.troopsout = true;
                    return true;
                }

                if (this.Owner.TroopList.Count == 0)
                {
                    this.troopsout = true;
                    return true;
                }
                if (this.Owner.GetHangars().Where(troopbay => troopbay.IsTroopBay).Count() == 0)
                {
                    this.troopsout = true;
                    return true;
                }
                if (this.Owner.TroopList.Where(loyalty => loyalty.GetOwner() != this.Owner.loyalty).Count() > 0)
                {
                    this.troopsout = true;
                    return true;
                }

                if (this.troopsout == true)
                {
                    foreach (ShipModule hangar in this.Owner.GetHangars())
                    {
                        if (hangar.IsTroopBay && (hangar.GetHangarShip() == null || hangar.GetHangarShip() != null && !hangar.GetHangarShip().Active) && hangar.hangarTimer <= 0)
                        {
                            this.troopsout = false;
                            break;

                        }

                    }
                }
                return this.troopsout;
            }
            set
            {
                this.troopsout = value;
                if (this.troopsout)
                {
                    this.Owner.ScrambleAssaultShips();
                    return;
                }
                this.Owner.RecoverAssaultShips();
            }
        }

        //added by gremlin : troop asssault planet
        public void OrderAssaultPlanet(Planet p)
        {
            this.State = AIState.AssaultPlanet;
            this.OrbitTarget = p;
            ArtificialIntelligence.ShipGoal shipGoal = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.LandTroop, Vector2.Zero, 0f)
            {
                TargetPlanet = OrbitTarget
            };
            this.orderqueue.EnterWriteLock();
            this.OrderQueue.Clear();
            this.OrderQueue.AddLast(shipGoal);
            this.orderqueue.ExitWriteLock();
        }
        public void OrderAssaultPlanetorig(Planet p)
        {
            this.State = AIState.AssaultPlanet;
            this.OrbitTarget = p;
        }

		private void DoDeploy(ArtificialIntelligence.ShipGoal shipgoal)
		{
			if (shipgoal.goal == null)
			{
				return;
			}
			if (shipgoal.goal.TetherTarget != Guid.Empty && Vector2.Distance(ArtificialIntelligence.universeScreen.PlanetsDict[shipgoal.goal.TetherTarget].Position + shipgoal.goal.TetherOffset, this.Owner.Center) > 200f)
			{
				shipgoal.goal.BuildPosition = ArtificialIntelligence.universeScreen.PlanetsDict[shipgoal.goal.TetherTarget].Position + shipgoal.goal.TetherOffset;
				this.OrderDeepSpaceBuild(shipgoal.goal);
				return;
			}
			Ship platform = ResourceManager.CreateShipAtPoint(shipgoal.goal.ToBuildUID, this.Owner.loyalty, shipgoal.goal.BuildPosition);
			if (platform == null)
			{
				return;
			}
			foreach (SpaceRoad road in this.Owner.loyalty.SpaceRoadsList)
			{
				foreach (RoadNode node in road.RoadNodesList)
				{
					if (node.Position != shipgoal.goal.BuildPosition)
					{
						continue;
					}
					node.Platform = platform;
					if (!StatTracker.SnapshotsDict.ContainsKey(ArtificialIntelligence.universeScreen.StarDate.ToString("#.0")))
					{
						continue;
					}
					NRO nro = new NRO()
					{
						Node = node.Position,
						Radius = 300000f,
						StarDateMade = ArtificialIntelligence.universeScreen.StarDate
					};
					StatTracker.SnapshotsDict[ArtificialIntelligence.universeScreen.StarDate.ToString("#.0")][EmpireManager.EmpireList.IndexOf(this.Owner.loyalty)].EmpireNodes.Add(nro);
				}
			}
			if (shipgoal.goal.TetherTarget != Guid.Empty)
			{
				platform.TetherToPlanet(ArtificialIntelligence.universeScreen.PlanetsDict[shipgoal.goal.TetherTarget]);
				platform.TetherOffset = shipgoal.goal.TetherOffset;
			}
			this.Owner.loyalty.GetGSAI().Goals.Remove(shipgoal.goal);
			this.Owner.QueueTotalRemoval();
		}

		private void DoEvadeCombat(float elapsedTime)
		{

            Vector2 AverageDirection = new Vector2();
            int count = 0;
            foreach (ArtificialIntelligence.ShipWeight ship in this.NearbyShips)
            {
                if (ship.ship.loyalty == this.Owner.loyalty || !ship.ship.loyalty.isFaction && !this.Owner.loyalty.GetRelations()[ship.ship.loyalty].AtWar)
                {
                    continue;
                }
                AverageDirection = AverageDirection + this.findVectorToTarget(this.Owner.Center, ship.ship.Center);
                count++;
            }
            if (count != 0)
            {
                AverageDirection = AverageDirection / (float)count;
                AverageDirection = Vector2.Normalize(AverageDirection);
                AverageDirection = Vector2.Negate(AverageDirection);
                
                
                {
                    AverageDirection = AverageDirection * 7500f;
                    this.ThrustTowardsPosition(AverageDirection + this.Owner.Center, elapsedTime, this.Owner.speed);
                }
            }
		}

		public void DoExplore(float elapsedTime)
		{
			this.HasPriorityOrder = true;
			this.IgnoreCombat = true;
			if (this.ExplorationTarget == null)
			{
				this.ExplorationTarget = this.Owner.loyalty.GetGSAI().AssignExplorationTarget(this.Owner);
				if (this.ExplorationTarget == null)
				{
					this.OrderQueue.Clear();
					this.State = AIState.AwaitingOrders;
					return;
				}
			}
			else if (this.DoExploreSystem(elapsedTime))
			{
                if (this.Owner.loyalty == ArtificialIntelligence.universeScreen.player)
                {
                    //added by gremlin  add shamatts notification here
                    string planetsInfo = "";
                    Dictionary<string, int> planetsTypesNumber = new Dictionary<string, int>();
                    SolarSystem system = this.ExplorationTarget;
                    if (system.PlanetList.Count > 0)
                    {
                        foreach (Planet planet in system.PlanetList)
                        {
                            // some planets don't have Type set and it is null
                            if (planet.Type == null)
                            {
                                planet.Type = "Other";
                            }

                            if (!planetsTypesNumber.ContainsKey(planet.Type))
                            {
                                planetsTypesNumber.Add(planet.Type, 1);
                            }
                            else
                            {
                                planetsTypesNumber[planet.Type] += 1;
                            }
                        }

                        foreach (KeyValuePair<string, int> pair in planetsTypesNumber)
                        {
                            planetsInfo = planetsInfo + "\n" + pair.Value + " " + pair.Key;
                        }
                    }

                    Notification cNote = new Notification()
                    {
                        Pause = false,
                        RelevantEmpire = this.Owner.loyalty,
                        Message = string.Concat(system.Name, " system explored."),
                        ReferencedItem1 = system,
                        IconPath = "NewUI/icon_planet_terran_01_mid",
                        Action = "SnapToExpandSystem",
                        ClickRect = new Rectangle(Planet.universeScreen.NotificationManager.NotificationArea.X, Planet.universeScreen.NotificationManager.NotificationArea.Y, 64, 64),
                        DestinationRect = new Rectangle(Planet.universeScreen.NotificationManager.NotificationArea.X, Planet.universeScreen.NotificationManager.NotificationArea.Y + Planet.universeScreen.NotificationManager.NotificationArea.Height - (Planet.universeScreen.NotificationManager.NotificationList.Count + 1) * 70, 64, 64)

                    };
                    cNote.Message = cNote.Message + planetsInfo;
                    if (system.combatTimer > 0)
                    {
                        cNote.Message += "\nCombat in system!!!";
                    }
                    if (system.OwnerList.Count > 0 && !system.OwnerList.Contains(this.Owner.loyalty))
                    {
                        cNote.Message += "\nContested system!!!";
                    }

                    foreach (Planet stuff in system.PlanetList)
                    {

                        foreach (Building tile in stuff.BuildingList)
                        {
                            if (tile.IsCommodity)
                            {

                                cNote.Message += "\n" + tile.Name + " on " + stuff.Name;
                                break;
                            }

                        }

                    }

                    AudioManager.PlayCue("sd_ui_notification_warning");
                    lock (GlobalStats.NotificationLocker)
                    {
                        Planet.universeScreen.NotificationManager.NotificationList.Add(cNote);
                    }
                }
                this.ExplorationTarget = null;
                            
			}
		}

		private bool DoExploreSystem(float elapsedTime)
		{
			this.SystemToPatrol = this.ExplorationTarget;
			if (this.PatrolRoute == null || this.PatrolRoute.Count == 0)
			{
				foreach (Planet p in this.SystemToPatrol.PlanetList)
				{
					this.PatrolRoute.Add(p);
				}
				if (this.SystemToPatrol.PlanetList.Count == 0)
				{
					return this.ExploreEmptySystem(elapsedTime, this.SystemToPatrol);
                    
                        

				}
			}
			else
			{
				this.PatrolTarget = this.PatrolRoute[this.stopNumber];
				if (this.PatrolTarget.ExploredDict[this.Owner.loyalty])
				{
					ArtificialIntelligence artificialIntelligence = this;
					artificialIntelligence.stopNumber = artificialIntelligence.stopNumber + 1;
					if (this.stopNumber == this.PatrolRoute.Count)
					{
						this.stopNumber = 0;
						this.PatrolRoute.Clear();
                       
						return true;
					}
				}
				else
				{
					this.MovePosition = this.PatrolTarget.Position;
					float Distance = Vector2.Distance(this.Owner.Center, this.MovePosition);
					if (Distance < 75000f)
					{
						this.PatrolTarget.system.ExploredDict[this.Owner.loyalty] = true;
					}
					if (Distance > 15000f)
					{
                        if (this.Owner.velocityMaximum > Distance && this.Owner.speed >= this.Owner.velocityMaximum)
                            this.Owner.speed = Distance;
                        this.ThrustTowardsPosition(this.MovePosition, elapsedTime, this.Owner.speed);
					}
					else if (Distance >= 5500f)
					{
                        if (this.Owner.velocityMaximum > Distance && this.Owner.speed >= this.Owner.velocityMaximum)
                            this.Owner.speed = Distance;
                        this.ThrustTowardsPosition(this.MovePosition, elapsedTime, this.Owner.speed);
					}
					else
					{
						this.ThrustTowardsPosition(this.MovePosition, elapsedTime, this.Owner.speed);
						if (Distance < 500f)
						{
							this.PatrolTarget.ExploredDict[this.Owner.loyalty] = true;
							ArtificialIntelligence artificialIntelligence1 = this;
							artificialIntelligence1.stopNumber = artificialIntelligence1.stopNumber + 1;
							if (this.stopNumber == this.PatrolRoute.Count)
							{
								this.stopNumber = 0;
								this.PatrolRoute.Clear();
								return true;
							}
						}
					}
				}
			}
			return false;
		}

		private void DoFleetArtillery(float elapsedTime)
		{
			this.findVectorToTarget(this.Owner.Center, this.Target.Center);
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, this.Target.Center);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, forward));
			if (Vector2.Distance(this.Owner.Center, this.Owner.fleet.Position + this.Owner.FleetOffset) > 2500f)
			{
				this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
				return;
			}
			if (angleDiff <= 0.02f)
			{
				this.DeRotate();
				return;
			}
			this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, right) > 0f ? 1f : -1f));
		}

		private void DoHoldPositionCombat(float elapsedTime)
		{
			if (this.Owner.Velocity.Length() > 0f)
			{
                if (this.Owner.engineState == Ship.MoveState.Warp)
                    this.Owner.HyperspaceReturn();
                Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
				Vector2 right = new Vector2(-forward.Y, forward.X);
				float angleDiff = (float)Math.Acos((double)Vector2.Dot(Vector2.Normalize(this.Owner.Velocity), forward));
				float facing = (Vector2.Dot(Vector2.Normalize(this.Owner.Velocity), right) > 0f ? 1f : -1f);
				if (angleDiff <= 0.2f)
				{
					this.Stop(elapsedTime);
					return;
				}
				this.RotateToFacing(elapsedTime, angleDiff, facing);
				return;
			}
			this.findVectorToTarget(this.Owner.Center, this.Target.Center);
            //renamed forward, right and anglediff
			Vector2 forward2 = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right2 = new Vector2(-forward2.Y, forward2.X);
			Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, this.Target.Center);
			float angleDiff2 = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, forward2));
			if (angleDiff2 <= 0.02f)
			{
				this.DeRotate();
				return;
			}
			this.RotateToFacing(elapsedTime, angleDiff2, (Vector2.Dot(VectorToTarget, right2) > 0f ? 1f : -1f));
		}

        
		private void DoLandTroop(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
			this.DoOrbit(goal.TargetPlanet, elapsedTime);
            //added by gremlin.
  
			if (this.Owner.Role == "troop" && this.Owner.TroopList.Count > 0 )
			{
                
                if (Vector2.Distance(goal.TargetPlanet.Position, this.Owner.Center) < 3500f  && goal.TargetPlanet.AssignTroopToTile(this.Owner.TroopList[0]))
                {//Vector2.Distance(goal.TargetPlanet.Position, this.Owner.Center) < 3500f
                    {
                       // GlobalStats.UILocker.EnterWriteLock();
                        this.Owner.QueueTotalRemoval();
                        //if (Ship.universeScreen.SelectedShip == this.Owner)
                        //    Ship.universeScreen.SelectedShip = null;
                        //GlobalStats.UILocker.EnterWriteLock();
                    }
                    return;
                }
			}
            else if (this.Owner.loyalty == goal.TargetPlanet.Owner || goal.TargetPlanet.GetGroundLandingSpots() == 0 || this.Owner.TroopList.Count <= 0 || (this.Owner.Role != "troop" && (this.Owner.GetHangars().Where(hangar => hangar.hangarTimer <= 0 && hangar.IsTroopBay).Count() == 0&& !this.Owner.hasTransporter)))//|| goal.TargetPlanet.GetGroundStrength(this.Owner.loyalty)+3 > goal.TargetPlanet.GetGroundStrength(goal.TargetPlanet.Owner)*1.5)
			{
				if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
				{
					this.HadPO = true;
				}
				this.HasPriorityOrder = false;
                this.State = this.DefaultAIState;
				this.OrderQueue.Clear();
                System.Diagnostics.Debug.WriteLine("Do Land Troop: Troop Assault Canceled");
			}
            else if (Vector2.Distance(goal.TargetPlanet.Position, this.Owner.Center) < 3500f)                
			{
				List<Troop> ToRemove = new List<Troop>();
                //if (Vector2.Distance(goal.TargetPlanet.Position, this.Owner.Center) < 3500f)
				{
                    //Get limit of troops to land
                    int LandLimit = this.Owner.GetHangars().Where(hangar => hangar.hangarTimer <= 0 && hangar.IsTroopBay).Count();
                    foreach (ShipModule module in this.Owner.Transporters.Where(module => module.TransporterTimer <= 1f))
                        LandLimit += module.TransporterTroopLanding;
                    //Land troops
                    foreach (Troop troop in this.Owner.TroopList)
                    {
                        if (troop == null || troop.GetOwner() != this.Owner.loyalty)
                            continue;
                        if (goal.TargetPlanet.AssignTroopToTile(troop))
                        {
                            ToRemove.Add(troop);
                            LandLimit--;
                            if (LandLimit < 1)
                                break;
                        }
                        else
                            break;
                    }
                    //Clear out Troops
                    if (ToRemove.Count > 0)
                    {
                        bool flag; // = false;
                        foreach (Troop RemoveTroop in ToRemove)
                        {
                            flag = false;
                            foreach (ShipModule module in this.Owner.GetHangars())
                            {
                                if (module.hangarTimer < module.hangarTimerConstant)
                                {
                                    module.hangarTimer = module.hangarTimerConstant;
                                    flag = true;
                                    break;
                                }
                            }
                            if (flag)
                                continue;
                            foreach (ShipModule module in this.Owner.Transporters)
                                if (module.TransporterTimer < module.TransporterTimerConstant)
                                {
                                    module.TransporterTimer = module.TransporterTimerConstant;
                                    flag = true;
                                    break;
                                }
                        }
                                //module.TransporterTimer = module.TransporterTimerConstant;
                            foreach (Troop to in ToRemove)
                                this.Owner.TroopList.Remove(to);
                        
                    }
				}
			}
		}

        private void DoNonFleetArtillery(float elapsedTime)
        {
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
            Vector2 right = new Vector2(-forward.Y, forward.X);
            Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, this.Target.Center);
            float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, forward));
            float DistanceToTarget = Vector2.Distance(this.Owner.Center, this.Target.Center);
            float minRangeMod = this.Owner.maxWeaponsRange < 2000 ? .5f : .75f;
            float maxRangeMod = this.Owner.maxWeaponsRange < 2000 ? .75f : .9f;
            if (DistanceToTarget > this.Owner.maxWeaponsRange * maxRangeMod + this.Target.Radius)
            {
                this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
                return;
            }

            if (DistanceToTarget < this.Owner.maxWeaponsRange * minRangeMod && Vector2.Distance(this.Owner.Center + (this.Owner.Velocity * elapsedTime), this.Target.Center) < DistanceToTarget)
            {
                Ship owner = this.Owner;
                owner.Velocity = owner.Velocity + (Vector2.Normalize(-forward) * (elapsedTime * this.Owner.velocityMaximum));
            }
            if (angleDiff <= 0.02f)
            {
                this.DeRotate();
                return;
            }
            this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, right) > 0f ? 1f : -1f));
        }

        private void DoNonFleetBroadsideRight(float elapsedTime)
        {
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
            Vector2 right = new Vector2(-forward.Y, forward.X);
            Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, this.Target.Center);
            float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, right));
            float DistanceToTarget = Vector2.Distance(this.Owner.Center, this.Target.Center);
            if (DistanceToTarget > this.Owner.maxWeaponsRange)
            {
                this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
                return;
            }
            if (DistanceToTarget < this.Owner.maxWeaponsRange * 0.70f && Vector2.Distance(this.Owner.Center + (this.Owner.Velocity * elapsedTime), this.Target.Center) < DistanceToTarget)
            {
                Ship owner = this.Owner;
                this.Owner.Velocity = Vector2.Zero;
                //owner.Velocity = owner.Velocity + (Vector2.Normalize(-left) * (elapsedTime * this.Owner.velocityMaximum));
            }
            if (angleDiff <= 0.02f)
            {
                this.DeRotate();
                return;
            }
            this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, forward) > 0f ? -1f : 1f));
        }

        private void DoNonFleetBroadsideLeft(float elapsedTime)
        {
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
            Vector2 right = new Vector2(-forward.Y, forward.X);
            Vector2 left = new Vector2(forward.Y, -forward.X);
            Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, this.Target.Center);
            float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, left));
            float DistanceToTarget = Vector2.Distance(this.Owner.Center, this.Target.Center);
            if (DistanceToTarget > this.Owner.maxWeaponsRange)
            {
                this.ThrustTowardsPosition(this.Target.Center, elapsedTime, this.Owner.speed);
                return;
            }
            if (DistanceToTarget < this.Owner.maxWeaponsRange * 0.70f && Vector2.Distance(this.Owner.Center + (this.Owner.Velocity * elapsedTime), this.Target.Center) < DistanceToTarget)
            {
                Ship owner = this.Owner;
                this.Owner.Velocity = Vector2.Zero;
                //owner.Velocity = owner.Velocity + (Vector2.Normalize(-left) * (elapsedTime * this.Owner.velocityMaximum));
            }
            if (angleDiff <= 0.02f)
            {
                this.DeRotate();
                return;
            }
            this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, forward) > 0f ? 1f : -1f));
        }

        //added by gremlin devksmod doorbit
        //private void DoOrbit(Planet OrbitTarget, float elapsedTime)
        //{            
        //    if (this.findNewPosTimer > 0f)
        //    {
        //        ArtificialIntelligence artificialIntelligence = this;
        //        artificialIntelligence.findNewPosTimer = artificialIntelligence.findNewPosTimer - elapsedTime;

        //    }
        //    else
        //    {
        //        this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 2500f);
        //        if (Vector2.Distance(this.OrbitPos, this.Owner.Center) < 1500f)
        //        {
        //            ArtificialIntelligence orbitalAngle = this;
        //            orbitalAngle.OrbitalAngle = orbitalAngle.OrbitalAngle + 15f;
        //            if (this.OrbitalAngle >= 360f)
        //            {
        //                ArtificialIntelligence orbitalAngle1 = this;
        //                orbitalAngle1.OrbitalAngle = orbitalAngle1.OrbitalAngle - 360f;
        //            }
        //            this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 2500f);
        //            if (this.inOrbit == false) this.inOrbit = true;
        //        }
        //        this.findNewPosTimer = 1.5f;

        //    }
        //    float single = Vector2.Distance(this.Owner.Center, this.OrbitPos);
        //    if (single < 7500f)
        //    {
        //        this.Owner.HyperspaceReturn();
        //        if (this.State != AIState.Bombard && this.State!=AIState.AssaultPlanet && this.State != AIState.BombardTroops && this.State!=AIState.Boarding && !this.IgnoreCombat)
        //        {
        //            this.HasPriorityOrder = false;
        //        }

        //    }
        //    if (single <= 15000f)
        //    {
        //        if (this.Owner.speed > 150f && this.Owner.engineState != Ship.MoveState.Warp)
        //        {
        //            this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, 150f);//this.Owner.speed / 3.5f);
        //            return;
        //        }
        //        if (this.Owner.engineState != Ship.MoveState.Warp)
        //        {
        //            this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
        //        }
        //        return;
        //    }
        //    Vector2 vector2 = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, OrbitTarget.Position));
        //    Vector2 vector21 = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
        //    Vector2 vector22 = new Vector2(-vector21.Y, vector21.X);
        //    Math.Acos((double)Vector2.Dot(vector2, vector21));
        //    Vector2.Dot(vector2, vector22);
        //    this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
        //}

        private void DoOrbit(Planet OrbitTarget, float elapsedTime)
        {
            float radius = OrbitTarget.ObjectRadius + this.Owner.Radius + 1500f;
            if (this.Owner.velocityMaximum == 0)
                return;
            float distanceToOrbitSpot = Vector2.Distance(this.OrbitPos, this.Owner.Center);          
            Vector2 test = Vector2.Subtract(this.Owner.Center, this.OrbitPos);
            if ((double)this.findNewPosTimer <= 0.0)//|| distanceToOrbitSpot <1500)
            {

                if (distanceToOrbitSpot < radius || this.Owner.speed == 0)
                {
                    
                    this.OrbitalAngle += MathHelper.ToDegrees((float)Math.Asin(this.Owner.yBankAmount*10));//   this.Owner.rotationRadiansPerSecond > 1 ? 1 : this.Owner.rotationRadiansPerSecond )) ;//< .1f ? .1f : this.Owner.rotationRadiansPerSecond));//* elapsedTime);// MathHelper.ToDegrees((float)Math.Asin((double)this.Owner.rotationRadiansPerSecond / 1500.0));
                    if ((double)this.OrbitalAngle >= 360.0)
                        this.OrbitalAngle -= 360f;
                }
                this.findNewPosTimer =  elapsedTime * 10;// this.Owner.rotationRadiansPerSecond > 1 ? 1 : this.Owner.rotationRadiansPerSecond;///< .1f ? .1f : this.Owner.rotationRadiansPerSecond) ;// this.Owner.rotationRadiansPerSecond;// elapsedTime;// 1; //1.5
                this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, radius);// 1500 ); //2500f //OrbitTarget.ObjectRadius +1000 + this.Owner.Radius);// 2500f);
            }
            else
                this.findNewPosTimer -= elapsedTime;
            float num1 = distanceToOrbitSpot;// Vector2.Distance(this.Owner.Center, this.OrbitPos);
            if ((double)num1 < 7500.0)
            {
                this.Owner.HyperspaceReturn();
                if (this.State != AIState.Bombard)
                    this.HasPriorityOrder = false;
            }
            if (num1 > 15000.0)
            {

                this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
            }
            else if //(num1 < 5000 &&  this.Owner.engineState != Ship.MoveState.Warp) 
                (num1 < 5000 && Vector2.Distance(this.Owner.Center, OrbitTarget.Position) < radius+1000f && this.Owner.engineState != Ship.MoveState.Warp)    //1200.0 && this.Owner.engineState != Ship.MoveState.Warp) (double)this.Owner.speed > 50&&
            {

                float opt = num1;// Vector2.Distance(this.Owner.Center, this.OrbitPos);
                this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
            }
            else
            {
                this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);// > 50 ? 50 : this.Owner.speed);
            }
        }

        private void DoOrbitBut(Planet OrbitTarget, float elapsedTime)
        {
            if(this.OrbitPos !=Vector2.Zero)
            this.OrbitPos += OrbitTarget.Position;
            float distanceToOrbitSpot = Vector2.Distance(OrbitTarget.Position, this.Owner.Center);
            if ((this.findNewPosTimer <= 0.0 && distanceToOrbitSpot < 5000) || this.OrbitPos == Vector2.Zero)
            {
                this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 1500); //2500f //OrbitTarget.ObjectRadius +1000 + this.Owner.Radius);// 2500f);
                if (Vector2.Distance(this.OrbitPos, this.Owner.Center) < 1500.0)
                {
                    this.OrbitalAngle = (int)this.OrbitalAngle + 15f;
                    if (this.OrbitalAngle >= 360.0)
                        this.OrbitalAngle -= 360f;
                    this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 2500f); //OrbitTarget.ObjectRadius + 1000 + this.Owner.Radius);// 2500f);
                }
                this.findNewPosTimer = 1f;
            }
            else
            {
                this.findNewPosTimer -= elapsedTime;
                if (OrbitPos != null && Vector2.Distance(OrbitTarget.Position, this.OrbitPos) > 15000)
                    this.OrbitPos = Vector2.Zero;
            }
            float num1 = Vector2.Distance(this.Owner.Center, this.OrbitPos);
            if (num1 < 7500.0)
            {
                this.Owner.HyperspaceReturn();
                if (this.State != AIState.Bombard)
                    this.HasPriorityOrder = false;
            }
            if (num1 > 15000.0)
            {
                this.ThrustTowardsPosition(OrbitTarget.Position, elapsedTime, this.Owner.speed);
            }
            else if (this.Owner.speed > 50 && num1 < 5000 && this.Owner.engineState != Ship.MoveState.Warp)    //1200.0 && this.Owner.engineState != Ship.MoveState.Warp)
            {
                float maxSpeed = this.Owner.velocityMaximum;
                this.Owner.speed = maxSpeed / (OrbitTarget.OrbitalRadius * (float)Math.PI * 2 * elapsedTime);   //      this.Owner.GetSTLSpeed() > 200 ? 200 : this.Owner.speed;

                this.RotateToFaceMovePosition(elapsedTime, this.OrbitPos);
                this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
            }
            else
            {
                if (this.Owner.engineState == Ship.MoveState.Warp)
                    return;

                this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);// > 50 ? 50 : this.Owner.speed);
            }
        }
		private void DoOrbitNoWarp(Planet OrbitTarget, float elapsedTime)
		{
			if (this.findNewPosTimer > 0f)
			{
				ArtificialIntelligence artificialIntelligence = this;
				artificialIntelligence.findNewPosTimer = artificialIntelligence.findNewPosTimer - elapsedTime;
			}
			else
			{
				this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 2500f);
				if (Vector2.Distance(this.OrbitPos, this.Owner.Center) < 1500f)
				{
					ArtificialIntelligence orbitalAngle = this;
					orbitalAngle.OrbitalAngle = orbitalAngle.OrbitalAngle + 15f;
					if (this.OrbitalAngle >= 360f)
					{
						ArtificialIntelligence orbitalAngle1 = this;
						orbitalAngle1.OrbitalAngle = orbitalAngle1.OrbitalAngle - 360f;
					}
					this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, OrbitTarget.Position, 2500f);
				}
				this.findNewPosTimer = 0.5f;
			}
			Vector2.Distance(this.Owner.Center, OrbitTarget.Position);
			if (this.Owner.speed > 1200f)
			{
				this.MoveTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed / 3.5f);
				return;
			}
			this.MoveTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed / 2f);
		}

		private void DoRebase(ArtificialIntelligence.ShipGoal Goal)
		{
			if (this.Owner.TroopList.Count == 0)
			{
				this.Owner.QueueTotalRemoval();
			}
			else if (Goal.TargetPlanet.AssignTroopToTile(this.Owner.TroopList[0]))
			{
				this.Owner.TroopList.Clear();
				this.Owner.QueueTotalRemoval();
				return;
			}
		}

		private void DoRefitORIG(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
			QueueItem qi = new QueueItem()
			{
				isShip = true,
				productionTowards = 0f,
				sData = ResourceManager.ShipsDict[goal.VariableString].GetShipData()
			};
			if (qi.sData == null)
			{
				this.OrderQueue.Clear();
				this.State = AIState.AwaitingOrders;
			}
			int cost = (int)(ResourceManager.ShipsDict[goal.VariableString].GetCost(this.Owner.loyalty) - this.Owner.GetCost(this.Owner.loyalty));
			if (cost < 0)
			{
				cost = 0;
			}
			cost = cost + 10 * (int)UniverseScreen.GamePaceStatic;
			qi.Cost = (float)cost;
			qi.isRefit = true;
			this.OrbitTarget.ConstructionQueue.Add(qi);
			this.Owner.QueueTotalRemoval();
		}

        //added by gremlin refit while in fleet
        private void DoRefit(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
        {
            QueueItem qi = new QueueItem()
            {
                isShip = true,
                productionTowards = 0f,
                sData = ResourceManager.ShipsDict[goal.VariableString].GetShipData()
            };

            if (qi.sData == null)
            {
                this.OrderQueue.Clear();
                this.State = AIState.AwaitingOrders;
            }
            int cost = (int)(ResourceManager.ShipsDict[goal.VariableString].GetCost(this.Owner.loyalty) - this.Owner.GetCost(this.Owner.loyalty));
            if (cost < 0)
            {
                cost = 0;
            }
            cost = cost + 10 * (int)UniverseScreen.GamePaceStatic;
            qi.Cost = (float)cost;
            qi.isRefit = true;
            //Added by McShooterz: refit keeps name and level
            if(this.Owner.VanityName != this.Owner.Name)
                qi.RefitName = this.Owner.VanityName;
            qi.sData.Level = (byte)this.Owner.Level;
            if (this.Owner.fleet != null)
            {

                FleetDataNode node = this.Owner.fleet.DataNodes.Where(thenode => thenode.GetShip() == this.Owner).First();

                Goal refitgoal = new Goal
                {
                    beingBuilt = ResourceManager.ShipsDict[goal.VariableString],

                    GoalName = "FleetRequisition",


                };
                refitgoal.Step = 1;
                refitgoal.beingBuilt.fleet = this.Owner.fleet;
                refitgoal.beingBuilt.RelativeFleetOffset = node.FleetOffset;
                node.GoalGUID = refitgoal.guid;
                refitgoal.SetFleet(this.Owner.fleet);
                refitgoal.SetPlanetWhereBuilding(this.OrbitTarget);

                this.Owner.loyalty.GetGSAI().Goals.Add(refitgoal);


                qi.Goal = refitgoal;
            }
            this.OrbitTarget.ConstructionQueue.Add(qi);
            this.Owner.QueueTotalRemoval();
        }

		private void DoRepairDroneLogic(Weapon w)
		{
            try
            {
                if (this.Owner.loyalty.GetShips().Where<Ship>((Ship ship) =>
                    {
                        if (ship.Health / ship.HealthMax >= 0.95f || !ship.Active)
                        {
                            return false;
                        }
                        return Vector2.Distance(this.Owner.Center, ship.Center) < 20000f;
                    }).Count<Ship>() == 0)
                {
                    return;
                }
            }
            catch
            {
                return;
            }
            //bool flag = false;
            //for (int x = 0; x < this.Owner.loyalty.GetShips().Count; x++)
            //{
            //    Ship ship;
            //    try
            //    {
            //        ship = this.Owner.loyalty.GetShips()[x];
            //    }
            //    catch { continue; }
            //    if (ship.Health == ship.HealthMax || ship.Health / ship.HealthMax >= 0.95f)
            //        continue;
            //    if (Vector2.Distance(this.Owner.Center, ship.Center) < 20000f)
            //    {
            //        flag = true;
            //        break;
            //    }

            //}

            //if (!flag)
            //    return;
			using (IEnumerator<Ship> enumerator = this.Owner.GetAI().FriendliesNearby.Where<Ship>((Ship ship) => {
				if (Vector2.Distance(this.Owner.Center, ship.Center) >= 20000f || !ship.Active)
				{
					return false;
				}
				return ship.Health / ship.HealthMax < 0.95f;
			}).OrderBy<Ship, float>((Ship ship) => Vector2.Distance(this.Owner.Center, ship.Center)).GetEnumerator())
			{
				if (enumerator.MoveNext())
				{
					Ship friendliesNearby = enumerator.Current;
					Vector2 target = this.findVectorToTarget(w.Center, friendliesNearby.Center);
					target.Y = target.Y * -1f;
					w.FireDrone(Vector2.Normalize(target));
				}
			}
		}

        private void DoRepairBeamLogic(Weapon w)
        {
            //foreach (Ship ship in w.GetOwner().loyalty.GetShips()
            foreach (Ship ship in this.FriendliesNearby
                .Where(ship => ship.Active && ship != w.GetOwner() 
                    && ship.Health / ship.HealthMax <.9f
                    && Vector2.Distance(this.Owner.Center, ship.Center) <= w.Range + 500f)
                    .OrderBy(ship => ship.Health))
            {
                if (ship != null)
                {
                    w.FireTargetedBeam(ship);
                    return;
                }
            }
        }

        private void DoOrdinanceTransporterLogic(ShipModule module)
        {
            foreach (Ship ship in module.GetParent().loyalty.GetShips().Where(ship => Vector2.Distance(this.Owner.Center, ship.Center) <= module.TransporterRange + 500f && ship.Ordinance < ship.OrdinanceMax && !ship.hasOrdnanceTransporter).OrderBy(ship => ship.Ordinance).ToList())
            {
                if (ship != null)
                {
                    module.TransporterTimer = module.TransporterTimerConstant;
                    float TransferAmount = 0f;
                    //check how much can be taken
                    if (module.TransporterOrdnance > module.GetParent().Ordinance)
                        TransferAmount = module.GetParent().Ordinance;
                    else
                        TransferAmount = module.TransporterOrdnance;
                    //check how much can be given
                    if (TransferAmount > ship.OrdinanceMax - ship.Ordinance)
                        TransferAmount = ship.OrdinanceMax - ship.Ordinance;
                    //Transfer
                    ship.Ordinance += TransferAmount;
                    module.GetParent().Ordinance -= TransferAmount;
                    module.GetParent().PowerCurrent -= module.TransporterPower * (TransferAmount / module.TransporterOrdnance);
                    if(this.Owner.InFrustum && ResourceManager.SoundEffectDict.ContainsKey("transporter"))
                    {
                        GameplayObject.audioListener.Position = ShipModule.universeScreen.camPos;
                        AudioManager.PlaySoundEffect(ResourceManager.SoundEffectDict["transporter"], GameplayObject.audioListener, module.GetParent().emitter, 0.5f);
                    }
                    return;
                }
            }
        }

        private void DoAssaultTransporterLogic(ShipModule module)
        {
            foreach (ArtificialIntelligence.ShipWeight ship in this.NearbyShips.Where(Ship => Ship.ship.loyalty != null && Ship.ship.loyalty != this.Owner.loyalty && Ship.ship.shield_power <= 0 && Vector2.Distance(this.Owner.Center, Ship.ship.Center) <= module.TransporterRange + 500f).OrderBy(Ship => Vector2.Distance(this.Owner.Center, Ship.ship.Center)))
            {
                if (ship != null)
                {
                    byte TroopCount = 0;
                    bool Transported = false;
                    for (byte i = 0; i < this.Owner.TroopList.Count(); i++)
                    {
                        if (this.Owner.TroopList[i] == null)
                            continue;
                        if (this.Owner.TroopList[i].GetOwner() == this.Owner.loyalty)
                        {
                            ship.ship.TroopList.Add(this.Owner.TroopList[i]);
                            this.Owner.TroopList.Remove(this.Owner.TroopList[i]);
                            TroopCount++;
                            Transported = true;
                        }
                        if (TroopCount == module.TransporterTroopAssault)
                            break;
                    }
                    if (Transported)
                    {
                        module.TransporterTimer = module.TransporterTimerConstant;
                        if (this.Owner.InFrustum && ResourceManager.SoundEffectDict.ContainsKey("transporter"))
                        {
                            GameplayObject.audioListener.Position = ShipModule.universeScreen.camPos;
                            AudioManager.PlaySoundEffect(ResourceManager.SoundEffectDict["transporter"], GameplayObject.audioListener, module.GetParent().emitter, 0.5f);
                        }
                        return;
                    }
                }
            }
        }



		private void DoResupply(float elapsedTime)
		{
			switch (this.resupplystep)
			{
				case 0:
				{
					List<Planet> potentials = new List<Planet>();
                    this.Owner.loyalty.GetPlanets().thisLock.EnterReadLock();
					foreach (Planet p in this.Owner.loyalty.GetPlanets())
					{
						if (!p.HasShipyard)
						{
							continue;
						}
						potentials.Add(p);
					}
                    this.Owner.loyalty.GetPlanets().thisLock.ExitReadLock();
					if (potentials.Count <= 0)
					{
						break;
					}
					IOrderedEnumerable<Planet> sortedList = 
						from planet in potentials
						orderby Vector2.Distance(this.Owner.Center, planet.Position)
						select planet;
					this.resupplyTarget = sortedList.ElementAt<Planet>(0);
					this.resupplystep = 1;
					return;
				}
				case 1:
				{
					this.DoOrbit(this.resupplyTarget, elapsedTime);
					if (this.Owner.Ordinance != this.Owner.OrdinanceMax || this.Owner.Health != this.Owner.HealthMax || Vector2.Distance(this.resupplyTarget.Position, this.Owner.Center) >= 7500f)
					{
						break;
					}
					this.State = AIState.AwaitingOrders;
					if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
					{
						this.HadPO = true;
					}
					this.HasPriorityOrder = false;
					break;
				}
				default:
				{
					return;
				}
			}
		}

		private void DoReturnToHangar(float elapsedTime)
		{
			if (this.Owner.Mothership == null || !this.Owner.Mothership.Active)
			{
				this.OrderQueue.Clear();
				return;
			}
			this.ThrustTowardsPosition(this.Owner.Mothership.Center, elapsedTime, this.Owner.speed);
			if (Vector2.Distance(this.Owner.Center, this.Owner.Mothership.Center) < 1000f)
			{
				if (this.Owner.Mothership.TroopCapacity > this.Owner.Mothership.TroopList.Count && this.Owner.TroopList.Count == 1)
				{
					this.Owner.Mothership.TroopList.Add(this.Owner.TroopList[0]);
				}
				this.Owner.QueueTotalRemoval();
                foreach (ShipModule hangar in this.Owner.Mothership.GetHangars())
                {
                    if (hangar.GetHangarShip() != this.Owner)
                    {
                        continue;
                    }
                    //added by gremlin: prevent fighters from relaunching immediatly after landing.
                    float rearmTime = this.Owner.Health;
                    rearmTime += this.Owner.Ordinance;
                    rearmTime += this.Owner.PowerCurrent;
                    rearmTime += this.Owner.shield_power;
                    rearmTime /= (this.Owner.HealthMax + this.Owner.OrdinanceMax + this.Owner.shield_max + this.Owner.PowerStoreMax);
                    float rearmConstant = (hangar.hangarTimerConstant * .5f);
                    rearmTime = rearmConstant * rearmTime;
                    rearmTime = (rearmConstant - rearmConstant * rearmTime) + hangar.hangarTimer > 0 ? hangar.hangarTimer : 0;
                    hangar.SetHangarShip(null);
                    hangar.hangarTimer = rearmTime; //0f;
                    hangar.installedSlot.HangarshipGuid = Guid.Empty;
                }
			}
		}

		private void DoSupplyShip(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
			if (this.EscortTarget == null || !this.EscortTarget.Active)
			{
				this.OrderQueue.Clear();
				this.OrderReturnToHangar();
				return;
			}
			this.ThrustTowardsPosition(this.EscortTarget.Center, elapsedTime, this.Owner.speed);
			if (Vector2.Distance(this.Owner.Center, this.EscortTarget.Center) < this.EscortTarget.Radius + 300f)
			{
				Ship escortTarget = this.EscortTarget;
				escortTarget.Ordinance = escortTarget.Ordinance + goal.VariableNumber;
				if (this.EscortTarget.Ordinance > this.EscortTarget.OrdinanceMax)
				{
					this.EscortTarget.Ordinance = this.EscortTarget.OrdinanceMax;
				}
				this.OrderQueue.Clear();
				this.Owner.QueueTotalRemoval();
				if (this.Owner.Mothership != null)
				{
					foreach (ShipModule hangar in this.Owner.Mothership.GetHangars())
					{
						if (hangar.GetHangarShip() != this.Owner)
						{
							continue;
						}
						hangar.hangarTimer = 1f;
					}
				}
			}
		}

		private void DoSystemDefense(float elapsedTime)
		{
            //if (this.Owner.InCombat || this.State == AIState.Intercept)
                
            //        return;
      
            if (this.SystemToDefend == null)
			{
				this.SystemToDefend = this.Owner.GetSystem();
               
			}
           //if(this.Owner.GetSystem() != this.SystemToDefend)
               
               this.AwaitOrders(elapsedTime);
           //this.State = AIState.AwaitingOrders;

                
		}

		private void DoTroopToShip(float elapsedTime)
		{
			if (this.EscortTarget == null || !this.EscortTarget.Active)
			{
				this.OrderQueue.Clear();
				return;
			}
			this.MoveTowardsPosition(this.EscortTarget.Center, elapsedTime);
			if (Vector2.Distance(this.Owner.Center, this.EscortTarget.Center) < this.EscortTarget.Radius +300f)
			{
				if (this.EscortTarget.TroopCapacity > this.EscortTarget.TroopList.Count)
				{
					this.EscortTarget.TroopList.Add(this.Owner.TroopList[0]);
					this.Owner.QueueTotalRemoval();
					return;
				}
				this.OrbitShip(this.EscortTarget, elapsedTime);
			}
		}

		private void DropoffGoods()
		{
			if (this.Owner.loyalty.data.Traits.Mercantile > 0f)
			{
				this.Owner.loyalty.AddTradeMoney(this.Owner.CargoSpace_Used * this.Owner.loyalty.data.Traits.Mercantile);
			}
            if (this.end != null)
            {
                if (this.FoodOrProd == "Food")
                {
                    int maxfood = (int)this.end.MAX_STORAGE - (int)this.end.FoodHere;
                    if (this.end.FoodHere + this.Owner.CargoSpace_Used <= this.end.MAX_STORAGE)
                    {
                        Planet foodHere = this.end;
                        foodHere.FoodHere = foodHere.FoodHere + (float)((int)this.Owner.CargoSpace_Used);
                        this.Owner.GetCargo()["Food"] = 0f;
                    }
                    else
                    {
                        Planet planet = this.end;
                        planet.FoodHere = planet.FoodHere + (float)maxfood;
                        Dictionary<string, float> cargo = this.Owner.GetCargo();
                        Dictionary<string, float> strs = cargo;
                        cargo["Food"] = strs["Food"] - (float)maxfood;
                    }
                }
                else if (this.FoodOrProd == "Prod")
                {
                    int maxprod = (int)this.end.MAX_STORAGE - (int)this.end.ProductionHere;
                    if (this.end.ProductionHere + this.Owner.CargoSpace_Used <= this.end.MAX_STORAGE)
                    {
                        Planet productionHere = this.end;
                        productionHere.ProductionHere = productionHere.ProductionHere + (float)((int)this.Owner.CargoSpace_Used);
                        this.Owner.GetCargo()["Production"] = 0f;
                    }
                    else
                    {
                        Planet productionHere1 = this.end;
                        productionHere1.ProductionHere = productionHere1.ProductionHere + (float)maxprod;
                        Dictionary<string, float> item = this.Owner.GetCargo();
                        Dictionary<string, float> strs1 = item;
                        item["Production"] = strs1["Production"] - (float)maxprod;
                    }
                }
            }
			this.OrderTrade();
			this.State = AIState.SystemTrader;
		}

		private void DropoffPassengers()
		{
            if (this.end == null)
            {
                this.OrderQueue.RemoveFirst();
                this.OrderTransportPassengers();
                return;
            }

            while (this.Owner.GetCargo()["Colonists_1000"] > 0f)
			{
				Dictionary<string, float> cargo = this.Owner.GetCargo();
				cargo["Colonists_1000"] = cargo["Colonists_1000"] - 1f;
				Planet population = this.end;
				population.Population = population.Population + (float)this.Owner.loyalty.data.Traits.PassengerModifier;
			}
			if (this.end.Population > this.end.MaxPopulation + this.end.MaxPopBonus)
			{
				this.end.Population = this.end.MaxPopulation + this.end.MaxPopBonus;
			}
			this.OrderQueue.RemoveFirst();
			this.OrderTransportPassengers();
		}

		private bool ExploreEmptySystem(float elapsedTime, SolarSystem system)
		{
			if (system.ExploredDict[this.Owner.loyalty])
			{
				return true;
			}
			this.MovePosition = system.Position;
			float Distance = Vector2.Distance(this.Owner.Center, this.MovePosition);
			if (Distance < 75000f)
			{
				system.ExploredDict[this.Owner.loyalty] = true;
				return true;
			}
			if (Distance > 75000f)
			{
				this.ThrustTowardsPosition(this.MovePosition, elapsedTime, this.Owner.speed);
			}
			return false;
		}

        private Vector2 findPointFromAngleAndDistance(Vector2 position, float angle, float distance)
        {
            Vector2 vector2 = new Vector2(0.0f, 0.0f);
            float num1 = angle;
            float num2 = distance;
            int num3 = 0;
            float num4 = 0.0f;
            float num5 = 0.0f;
            if ((double)num1 > 360.0)
                num1 -= 360f;
            if ((double)num1 < 90.0)
            {
                float num6 = (float)((double)(90f - num1) * 3.14159274101257 / 180.0);
                num4 = num2 * (float)Math.Sin((double)num6);
                num5 = num2 * (float)Math.Cos((double)num6);
                num3 = 1;
            }
            else if ((double)num1 > 90.0 && (double)num1 < 180.0)
            {
                float num6 = (float)((double)(num1 - 90f) * 3.14159274101257 / 180.0);
                num4 = num2 * (float)Math.Sin((double)num6);
                num5 = num2 * (float)Math.Cos((double)num6);
                num3 = 2;
            }
            else if ((double)num1 > 180.0 && (double)num1 < 270.0)
            {
                float num6 = (float)((double)(270f - num1) * 3.14159274101257 / 180.0);
                num4 = num2 * (float)Math.Sin((double)num6);
                num5 = num2 * (float)Math.Cos((double)num6);
                num3 = 3;
            }
            else if ((double)num1 > 270.0 && (double)num1 < 360.0)
            {
                float num6 = (float)((double)(num1 - 270f) * 3.14159274101257 / 180.0);
                num4 = num2 * (float)Math.Sin((double)num6);
                num5 = num2 * (float)Math.Cos((double)num6);
                num3 = 4;
            }
            if ((double)num1 == 0.0)
            {
                vector2.X = position.X;
                vector2.Y = position.Y - num2;
            }
            if ((double)num1 == 90.0)
            {
                vector2.X = position.X + num2;
                vector2.Y = position.Y;
            }
            if ((double)num1 == 180.0)
            {
                vector2.X = position.X;
                vector2.Y = position.Y + num2;
            }
            if ((double)num1 == 270.0)
            {
                vector2.X = position.X - num2;
                vector2.Y = position.Y;
            }
            if (num3 == 1)
            {
                vector2.X = position.X + num5;
                vector2.Y = position.Y - num4;
            }
            else if (num3 == 2)
            {
                vector2.X = position.X + num5;
                vector2.Y = position.Y + num4;
            }
            else if (num3 == 3)
            {
                vector2.X = position.X - num5;
                vector2.Y = position.Y + num4;
            }
            else if (num3 == 4)
            {
                vector2.X = position.X - num5;
                vector2.Y = position.Y - num4;
            }
            return vector2;
        }

		private Vector2 findPointFromAngleAndDistanceorg(Vector2 position, float angle, float distance)
		{

            double theta;
			Vector2 TargetPosition = new Vector2(0f, 0f);
			float gamma = angle;
			double D = distance;
			int gammaQuadrant = 0;
			double oppY = 0f;
			double adjX = 0f;
			if (gamma > 360f)
			{
				gamma = gamma - 360f;
			}
			if (gamma < 90f)
			{
				theta = 90f - gamma;
                theta = theta * Math.PI / 180.0;  //3.14159274f / 180f;
				oppY = D * Math.Sin(theta);
				adjX = D * Math.Cos(theta);
				gammaQuadrant = 1;
			}
			else if (gamma > 90f && gamma < 180f)
			{
				theta = gamma - 90f;
				theta = theta * 3.14159274f / 180f;
				oppY = D * Math.Sin(theta);
				adjX = D * Math.Cos(theta);
				gammaQuadrant = 2;
			}
			else if (gamma > 180f && gamma < 270f)
			{
				theta = 270f - gamma;
				theta = theta * 3.14159274f / 180f;
				oppY = D * Math.Sin(theta);
				adjX = D * Math.Cos(theta);
				gammaQuadrant = 3;
			}
			else if (gamma > 270f && gamma < 360f)
			{
				theta = gamma - 270f;
				theta = theta * 3.14159274f / 180f;
				oppY = D * Math.Sin(theta);
				adjX = D * Math.Cos(theta);
				gammaQuadrant = 4;
			}
			if (gamma == 0f)
			{
				TargetPosition.X = position.X;
				TargetPosition.Y = position.Y - (float)D;
			}
			if (gamma == 90f)
			{
                TargetPosition.X = position.X + (float)D;
				TargetPosition.Y = position.Y;
			}
			if (gamma == 180f)
			{
				TargetPosition.X = position.X;
                TargetPosition.Y = position.Y + (float)D;
			}
			if (gamma == 270f)
			{
                TargetPosition.X = position.X - (float)D;
				TargetPosition.Y = position.Y;
			}
			if (gammaQuadrant == 1)
			{
                TargetPosition.X = position.X + (float)adjX;
                TargetPosition.Y = position.Y - (float)oppY;
			}
			else if (gammaQuadrant == 2)
			{
                TargetPosition.X = position.X + (float)adjX;
                TargetPosition.Y = position.Y + (float)oppY;
			}
			else if (gammaQuadrant == 3)
			{
                TargetPosition.X = position.X - (float)adjX;
                TargetPosition.Y = position.Y + (float)oppY;
			}
			else if (gammaQuadrant == 4)
			{
                TargetPosition.X = position.X - (float)adjX;
                TargetPosition.Y = position.Y - (float)oppY;
			}
			return TargetPosition;
		}

		private Vector2 findPointFromAngleAndDistanceUsingRadians(Vector2 position, float angle, float distance)
		{
			float theta;
			Vector2 TargetPosition = new Vector2(0f, 0f);
			float gamma = MathHelper.ToDegrees(angle);
			float D = distance;
			int gammaQuadrant = 0;
			float oppY = 0f;
			float adjX = 0f;
			if (gamma > 360f)
			{
				gamma = gamma - 360f;
			}
			if (gamma < 90f)
			{
				theta = 90f - gamma;
				theta = theta * 3.14159274f / 180f;
				oppY = D * (float)Math.Sin((double)theta);
				adjX = D * (float)Math.Cos((double)theta);
				gammaQuadrant = 1;
			}
			else if (gamma > 90f && gamma < 180f)
			{
				theta = gamma - 90f;
				theta = theta * 3.14159274f / 180f;
				oppY = D * (float)Math.Sin((double)theta);
				adjX = D * (float)Math.Cos((double)theta);
				gammaQuadrant = 2;
			}
			else if (gamma > 180f && gamma < 270f)
			{
				theta = 270f - gamma;
				theta = theta * 3.14159274f / 180f;
				oppY = D * (float)Math.Sin((double)theta);
				adjX = D * (float)Math.Cos((double)theta);
				gammaQuadrant = 3;
			}
			else if (gamma > 270f && gamma < 360f)
			{
				theta = gamma - 270f;
				theta = theta * 3.14159274f / 180f;
				oppY = D * (float)Math.Sin((double)theta);
				adjX = D * (float)Math.Cos((double)theta);
				gammaQuadrant = 4;
			}
			if (gamma == 0f)
			{
				TargetPosition.X = position.X;
				TargetPosition.Y = position.Y - D;
			}
			if (gamma == 90f)
			{
				TargetPosition.X = position.X + D;
				TargetPosition.Y = position.Y;
			}
			if (gamma == 180f)
			{
				TargetPosition.X = position.X;
				TargetPosition.Y = position.Y + D;
			}
			if (gamma == 270f)
			{
				TargetPosition.X = position.X - D;
				TargetPosition.Y = position.Y;
			}
			if (gammaQuadrant == 1)
			{
				TargetPosition.X = position.X + adjX;
				TargetPosition.Y = position.Y - oppY;
			}
			else if (gammaQuadrant == 2)
			{
				TargetPosition.X = position.X + adjX;
				TargetPosition.Y = position.Y + oppY;
			}
			else if (gammaQuadrant == 3)
			{
				TargetPosition.X = position.X - adjX;
				TargetPosition.Y = position.Y + oppY;
			}
			else if (gammaQuadrant == 4)
			{
				TargetPosition.X = position.X - adjX;
				TargetPosition.Y = position.Y - oppY;
			}
			return TargetPosition;
		}

		private Vector2 findVectorBehindTarget(GameplayObject ship, float distance)
		{
			Vector2 vector2 = new Vector2(0f, 0f);
			Vector2 forward = new Vector2((float)Math.Sin((double)ship.Rotation), -(float)Math.Cos((double)ship.Rotation));
			forward = Vector2.Normalize(forward);
			return ship.Position - (forward * distance);
		}

		private Vector2 findVectorToTarget(Vector2 OwnerPos, Vector2 TargetPos)
		{
			Vector2 Vec2Target = new Vector2(0f, 0f)
			{
				X = -(OwnerPos.X - TargetPos.X),
				Y = OwnerPos.Y - TargetPos.Y
			};
			return Vec2Target;
		}

        public void FireOnTarget() //(float elapsedTime)
        {
            //Reasons not to fire
            //try
            {
                TargetShip = this.Target as Ship;
                Relationship enemy =null;
                if (!this.Owner.hasCommand ||this.Owner.engineState == Ship.MoveState.Warp || this.Owner.disabled || this.Owner .Weapons.Count==0 ||
                    ((TargetShip != null && !this.Owner.loyalty.isFaction) && (this.Owner.loyalty.GetRelations().TryGetValue(TargetShip.loyalty, out enemy)
                    && enemy != null && (enemy.Treaty_Peace || enemy.Treaty_Alliance || enemy.Treaty_NAPact))))
                {
                    return;
                }

                //Determine if there is something to shoot at
                if (this.BadGuysNear || this.Owner.InCombat)
                {
                    //Target is dead or dying, will need a new one.
                    if (this.Target != null && (!this.Target.Active || TargetShip != null && TargetShip.dying))
                    {
                        this.Target = null;
                        TargetShip = null;
                    }
                    this.TrackProjectiles.Clear();                                        
                    //update projectile list
                    {

                        if (this.Owner.GetSystem() != null)
                        {
                            //Find non friendly planets
                            foreach (Planet p in this.Owner.GetSystem().PlanetList)
                            {
                                if (p.Owner == this.Owner.loyalty)
                                    continue;
                                foreach (Projectile proj in p.Projectiles)
                                {
                                    this.TrackProjectiles.Add(proj);
                                }
                            }
                        }
                        {
                            foreach (Ship ship in PotentialTargets)
                            {

                                for (int i = 0; i < ship.Projectiles.Count; i++)
                                {
                                    Projectile proj;
                                    {
                                        proj = ship.Projectiles[i];
                                    }

                                    if (proj == null || !proj.Active || proj.Health <= 0 || !proj.weapon.Tag_Intercept)
                                        continue;
                                    this.TrackProjectiles.Add(proj);
                                }

                            }

                        }
                        this.TrackProjectiles = this.TrackProjectiles.OrderBy(prj => Vector2.Distance(this.Owner.Center, prj.Center)).ToList();

                    }
                    float lag = Ship.universeScreen.Lag;
                    //Go through each weapon
                    float index = 0; //count up weapons.
                    //save target ship if it is a ship.
                    this.TargetShip = this.Target as Ship;
                    //group of weapons into chunks per thread available
                    var source = Enumerable.Range(0, this.Owner.Weapons.Count).ToArray();
                            var rangePartitioner = Partitioner.Create(0, source.Length);
                    //handle each weapon group in parallel
                            Parallel.ForEach(rangePartitioner, (range, loopState) =>
                                           {
                                               //standard for loop through each weapon group.
                                               for (int T = range.Item1; T < range.Item2; T++)
                                               {
                                                   Weapon weapon = this.Owner.Weapons[T];
                                                   //Reasons for this weapon not to fire 
                                                   if ( !weapon.moduleAttachedTo.Active || weapon.timeToNextFire > 0f || !weapon.moduleAttachedTo.Powered || weapon.IsRepairDrone || weapon.isRepairBeam)
                                                   {
                                                       //continue;
                                                       return;
                                                   }
                                                   ShipModule moduletarget = weapon.fireTarget as ShipModule;
                                                   //if firing at the primary target mark weapon as firing on primary.
                                                   if (!(weapon.fireTarget is Projectile) &&( weapon.fireTarget == this.Target || (moduletarget !=null && (moduletarget.GetParent() as GameplayObject) ==this.Target)))
                                                       weapon.PrimaryTarget = true;                                                   
                                                    //check if weapon target as a gameplay object is still a valid target    
                                                   if (weapon.fireTarget !=null )
                                                   {
                                                       
                                                       if ( (weapon.PrimaryTarget && weapon.fireTarget != this.Target)                                                           
                                                           || !this.Owner.CheckIfInsideFireArc(weapon, weapon.fireTarget) 
                                                           //check here if the weapon can fire on main target.
                                                           || (this.Target !=null &&(!weapon.PrimaryTarget && !(weapon.fireTarget is Projectile) && this.Owner.CheckIfInsideFireArc(weapon, this.Target) ))                                                         
                                                           )
                                                       {
                                                           weapon.TargetChangeTimer = .5f;
                                                           weapon.fireTarget = null;
                                                           if (weapon.isBeam || weapon.isMainGun)
                                                               weapon.TargetChangeTimer = .90f;
                                                           if (weapon.isTurret)
                                                               weapon.TargetChangeTimer *= .5f;
                                                           if(weapon.Tag_PD)
                                                           {
                                                               weapon.TargetChangeTimer *= .5f;
                                                           }

                             
                                                           
                                                       }
                                                       
                                                   }
                                                   //if weapon target is null reset primary target and decrement target change timer.
                                                   if (weapon.fireTarget == null)
                                                   {
                                                       weapon.TargetChangeTimer -= 0.0167f;
                                                       if (weapon.PrimaryTarget != false)
                                                           weapon.PrimaryTarget = false;
                                                   }
                                                   //Reasons for this weapon not to fire                    
                                                   if (weapon.fireTarget == null && weapon.TargetChangeTimer >0 ) // ||!weapon.moduleAttachedTo.Active || weapon.timeToNextFire > 0f || !weapon.moduleAttachedTo.Powered || weapon.IsRepairDrone || weapon.isRepairBeam)
                                                   {
                                                       //continue;
                                                       return;
                                                   }
                                                   //main targeting loop. little check here to disable the whole thing for debugging.
                                                   if (true)
                                                   {
                                                       //Can this weapon fire on ships
                                                       if (this.BadGuysNear && !weapon.TruePD  )
                                                       {
                                                           //if there are projectile to hit and weapons that can shoot at them. do so. 
                                                           if(this.TrackProjectiles.Count >0 && weapon.Tag_PD)
                                                           {
                                                               for (int i = 0; i < this.TrackProjectiles.Count; i++)
                                                               {
                                                                   Projectile proj;
                                                                   {
                                                                       proj = this.TrackProjectiles[i];
                                                                   }

                                                                   if (proj == null || !proj.Active || proj.Health <= 0 || !proj.weapon.Tag_Intercept)
                                                                       continue;
                                                                   if (this.Owner.CheckIfInsideFireArc(weapon, proj as GameplayObject))
                                                                   {
                                                                       weapon.fireTarget = proj;
                                                                       break;
                                                                   }
                                                               }
                                                           }
                                                           //Is primary target valid
                                                           if (weapon.fireTarget == null)
                                                           if (this.Owner.CheckIfInsideFireArc(weapon, this.Target))
                                                           {
                                                               weapon.fireTarget = this.Target;
                                                               weapon.PrimaryTarget = true;
                                                           }

                                                           //Find alternate target to fire on
                                                           //this seems to be very expensive code. 
                                                           if (weapon.fireTarget == null)
                                                           {
                                                               //limit to one target per level.
                                                               for (int i = 0; i < this.PotentialTargets.Count && i < this.Owner.Level+1; i++) //
                                                               {
                                                                   Ship PotentialTarget = this.PotentialTargets[i];
                                                                   if (PotentialTarget == this.TargetShip
                                                                       || !this.Owner.CheckIfInsideFireArc(weapon, PotentialTarget))
                                                                   {
                                                                       continue;
                                                                   }
                                                                   weapon.fireTarget = PotentialTarget;
                                                                   break;

                                                               }
                                                           }
                                                           //If a ship was found to fire on, change to target an internal module if target is visible  || weapon.Tag_Intercept
                                                           if (weapon.fireTarget != null)
                                                           {
                                                               if (weapon.fireTarget is Ship && (GlobalStats.ForceFullSim || this.Owner.InFrustum || (weapon.fireTarget as Ship).InFrustum))// || (this.Owner.InFrustum || this.Target != null && TargetShip.InFrustum)))
                                                               {
                                                                   weapon.fireTarget = (weapon.fireTarget as Ship).GetRandomInternalModule(weapon);
                                                                   //weapon.fireTarget;// = fireTarget;
                                                               }

                                                           }
                                                       }
                                                       //No ship to target, check for projectiles
                                                       if (weapon.fireTarget == null && weapon.Tag_PD)
                                                       {

                                                          
                                                          

                                                           //projectile list is created in teh scan for combat combats method.
                                                           if (weapon.fireTarget == null)
                                                           {

                                                               for (int i = 0; i < this.TrackProjectiles.Count && i < this.Owner.Level+1;i++)
                                                               {
                                                                   Projectile proj;
                                                                   {
                                                                       proj = this.TrackProjectiles[i];
                                                                   }

                                                                   if (proj == null || !proj.Active || proj.Health <= 0 || !proj.weapon.Tag_Intercept)
                                                                       continue;
                                                                   if (this.Owner.CheckIfInsideFireArc(weapon, proj as GameplayObject))
                                                                   {
                                                                       weapon.fireTarget = proj;
                                                                       break;
                                                                   }
                                                               }



                                                           }




                                                       }

                                                   }
        

                                               }
                                           });
                    //this section actually fires the weapons. This whole firing section can be moved to some other area of the code. This code is very expensive. 
                    if(1==1)
                    foreach (Weapon weapon in this.Owner.Weapons)
                    {
                        if (weapon.fireTarget != null &&(weapon.moduleAttachedTo.Active && weapon.timeToNextFire <= 0f && weapon.moduleAttachedTo.Powered ))
                        {
                            if (!(weapon.fireTarget is Ship))
                            {
                                GameplayObject target = weapon.fireTarget;
                                if (weapon.isBeam)
                                    weapon.FireTargetedBeam(target);
                                else if (weapon.Tag_Guided)
                                {
                                    if (index > 10 && lag > .03 && !GlobalStats.ForceFullSim && (!weapon.Tag_Intercept) && (weapon.fireTarget is ShipModule))
                                        this.FireOnTargetNonVisible(weapon, (weapon.fireTarget as ShipModule).GetParent());
                                    else
                                        weapon.Fire(new Vector2((float)Math.Sin((double)this.Owner.Rotation + MathHelper.ToRadians(weapon.moduleAttachedTo.facing)), -(float)Math.Cos((double)this.Owner.Rotation + MathHelper.ToRadians(weapon.moduleAttachedTo.facing))), target);
                                    index++;
                                }
                                else
                                {
                                    if (index > 10 && lag > .03 && !GlobalStats.ForceFullSim && (weapon.fireTarget is ShipModule))
                                        this.FireOnTargetNonVisible(weapon, (weapon.fireTarget as ShipModule).GetParent());
                                    else
                                        CalculateAndFire(weapon, target, false);
                                    index++;
                                }
                            }
                            else
                                this.FireOnTargetNonVisible(weapon, weapon.fireTarget);
                        }
                    }

                }
            }
            //catch (Exception e)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine(e.InnerException); 
#endif

            }

            this.TargetShip = null;

        }

        public void CalculateAndFire(Weapon weapon, GameplayObject target, bool SalvoFire)
        {
            //moved this code a little lower as it does not work in some situations
            //if (this.Owner.speed == 0 && target.Velocity.Length() == 0)
            //{
            //    Vector2 fireatstationary = Vector2.Zero;
            //    fireatstationary = Vector2.Normalize(this.findVectorToTarget(weapon.Center, target.Center));
            //    if (SalvoFire)
            //        weapon.FireSalvo(fireatstationary, target);
            //    else
            //        weapon.Fire(fireatstationary, target);
            //    return;

            //}
            
            float distance = Vector2.Distance(weapon.Center, target.Center) + target.Velocity.Length()==0?0: 500;
            Vector2 dir = (Vector2.Normalize(this.findVectorToTarget(weapon.Center, target.Center)) * (weapon.ProjectileSpeed + this.Owner.Velocity.Length()));
            float timeToTarget = distance / dir.Length();
            Vector2 projectedPosition = target.Center;
            //this.moduleTarget = target as ShipModule;
            ShipModule moduleTarget = target as ShipModule;
            if (target is Projectile)
            {
                projectedPosition = target.Center + (target.Velocity * timeToTarget);
                distance = Vector2.Distance(weapon.Center, projectedPosition);
                dir = (Vector2.Normalize(this.findVectorToTarget(weapon.Center, projectedPosition)) * (weapon.ProjectileSpeed + this.Owner.Velocity.Length()));
                timeToTarget = distance / dir.Length();
                projectedPosition = target.Center + ((target.Velocity * timeToTarget) * 0.85f);
            }
            else if (moduleTarget !=null)
            {
                if (moduleTarget.GetParent().Velocity.Length() > 0.0f)
                projectedPosition = target.Center + ((target as ShipModule).GetParent().Velocity * timeToTarget);
                else
                    projectedPosition = target.Center ;
                if (projectedPosition != target.Center && (target as ShipModule).GetParent().Velocity.Length() <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("missing and correcting");
                    //moved docs target correction here. 
                    Vector2 fireatstationary = Vector2.Zero;
                    fireatstationary = Vector2.Normalize(this.findVectorToTarget(weapon.Center, target.Center));
                    if (SalvoFire)
                        weapon.FireSalvo(fireatstationary, target);
                    else
                        weapon.Fire(fireatstationary, target);
                    return;
                    
                }
                distance = Vector2.Distance(weapon.Center, projectedPosition);
                if (moduleTarget.GetParent().Velocity.Length() > 0.0f) 
                dir = (Vector2.Normalize(this.findVectorToTarget(weapon.Center, projectedPosition)) * (weapon.ProjectileSpeed + this.Owner.Velocity.Length()));
                else
                {
                    dir = this.findVectorToTarget(weapon.Center, projectedPosition);

                }
                timeToTarget = distance / dir.Length();
                projectedPosition = target.Center + (moduleTarget.GetParent().Velocity * timeToTarget);
            }


            dir = this.findVectorToTarget(weapon.Center, projectedPosition);
            dir.Y = dir.Y * -1f;
            if (moduleTarget ==null  || moduleTarget.GetParent().Velocity.Length() >0)
            dir = Vector2.Normalize(dir);
            


            if (SalvoFire)
                weapon.FireSalvo(dir, target);
            else
                weapon.Fire(dir, target);
        }

        private void FireOnTargetNonVisible(Weapon w, GameplayObject fireTarget)
        {
            if (this.Owner.Ordinance < w.OrdinanceRequiredToFire || this.Owner.PowerCurrent < w.PowerRequiredToFire)
            {
                return;
            }
            w.timeToNextFire = w.fireDelay;
            if (w.IsRepairDrone)
            {
                return;
            }
            if (TargetShip == null || !TargetShip.Active || TargetShip.dying || !w.TargetValid(TargetShip.Role)
                || TargetShip.engineState == Ship.MoveState.Warp || !this.Owner.CheckIfInsideFireArc(w, TargetShip))
                return;
            Ship owner = this.Owner;
            owner.Ordinance = owner.Ordinance - w.OrdinanceRequiredToFire;
            Ship powerCurrent = this.Owner;
            powerCurrent.PowerCurrent = powerCurrent.PowerCurrent - w.PowerRequiredToFire;
            powerCurrent.PowerCurrent -= w.BeamPowerCostPerSecond * w.BeamDuration;

            this.Owner.InCombatTimer = 15f;
            if (fireTarget is Projectile)
            {
                fireTarget.Damage(w.GetOwner(), w.DamageAmount);
                return;
            }
            if (!(fireTarget is Ship))
            {
                if (fireTarget is ShipModule)
                {
                    w.timeToNextFire = w.fireDelay;
                    IOrderedEnumerable<ModuleSlot> sortedList =
                        from slot in (fireTarget as ShipModule).GetParent().ExternalSlots
                        orderby Vector2.Distance(slot.module.Center, this.Owner.Center)
                        select slot;
                    float damage = w.DamageAmount;
                    if (w.isBeam)
                    {
                        damage = damage * 90f;
                    }
                    if (w.SalvoCount > 0)
                    {
                        damage = damage * (float)w.SalvoCount;
                    }
                    sortedList.First<ModuleSlot>().module.Damage(this.Owner, damage);
                }
                return;
            }
            w.timeToNextFire = w.fireDelay;
            if ((fireTarget as Ship).ExternalSlots.Count == 0)
            {
                (fireTarget as Ship).Die(null, true);
                return;
            }
            float nearest = 0;
            ModuleSlot ClosestES = null;
            foreach (ModuleSlot ES in (fireTarget as Ship).ExternalSlots)
            {
                if (ES.module.ModuleType == ShipModuleType.Dummy || !ES.module.Active || ES.module.Health <= 0)
                    continue;
                float temp = Vector2.Distance(ES.module.Center, w.GetOwner().Center);
                if (nearest == 0 || temp < nearest)
                {
                    nearest = temp;
                    ClosestES = ES;
                }
            }
            if (ClosestES == null)
                return;
            // List<ModuleSlot> 
            IEnumerable<ModuleSlot> ExternalSlots = (fireTarget as Ship).ExternalSlots.Where(close => close.module.Active && close.module.quadrant == ClosestES.module.quadrant && close.module.Health > 0);//.ToList();   //.OrderByDescending(shields=> shields.Shield_Power >0);//.ToList();
            if ((fireTarget as Ship).shield_power > 0f)
            {
                for (int i = 0; i < (fireTarget as Ship).GetShields().Count; i++)
                {
                    if ((fireTarget as Ship).GetShields()[i].Active && (fireTarget as Ship).GetShields()[i].shield_power > 0f)
                    {
                        float damage = w.DamageAmount;
                        if (w.isBeam)
                        {
                            damage = damage * 90f;
                        }
                        if (w.SalvoCount > 0)
                        {
                            damage = damage * (float)w.SalvoCount;
                        }
                        (fireTarget as Ship).GetShields()[i].Damage(this.Owner, damage);
                        return;
                    }
                }
                return;
            }
            //this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(0f, 100f) <= 50f ||
            if (ExternalSlots.ElementAt(0).module.shield_power > 0f)
            {
                for (int i = 0; i < ExternalSlots.Count(); i++)
                {
                    if (ExternalSlots.ElementAt(i).module.Active && ExternalSlots.ElementAt(i).module.shield_power <= 0f)
                    {
                        float damage = w.DamageAmount;
                        if (w.isBeam)
                        {
                            damage = damage * 90f;
                        }
                        if (w.SalvoCount > 0)
                        {
                            damage = damage * (float)w.SalvoCount;
                        }
                        ExternalSlots.ElementAt(i).module.Damage(this.Owner, damage);
                        return;
                    }
                }
                return;
            }

            for (int i = 0; i < ExternalSlots.Count(); i++)
            {
                if (ExternalSlots.ElementAt(i).module.Active && ExternalSlots.ElementAt(i).module.shield_power <= 0f)
                {
                    float damage = w.DamageAmount;
                    if (w.isBeam)
                    {
                        damage = damage * 90f;
                    }
                    if (w.SalvoCount > 0)
                    {
                        damage = damage * (float)w.SalvoCount;
                    }
                    ExternalSlots.ElementAt(i).module.Damage(this.Owner, damage);
                    return;
                }
            }
        }
        
		private Vector2 GeneratePointOnCircle(float angle, Vector2 center, float radius)
		{
			return this.findPointFromAngleAndDistance(center, angle, radius);
		}

		public void GoColonize(Planet p)
		{
			this.State = AIState.Colonize;
			this.ColonizeTarget = p;
			this.GotoStep = 0;
		}

		public void GoColonize(Planet p, Goal g)
		{
			this.State = AIState.Colonize;
			this.ColonizeTarget = p;
			this.ColonizeGoal = g;
			this.GotoStep = 0;
			this.OrderColonization(p);
		}

		public void GoRebase(Planet p)
		{
			this.HasPriorityOrder = true;
			this.State = AIState.Rebase;
			this.OrbitTarget = p;
			this.findNewPosTimer = 0f;
			this.moveTimer = 0f;
			this.GotoStep = 0;
			this.HasPriorityOrder = true;
			this.MovePosition.X = p.Position.X;
			this.MovePosition.Y = p.Position.Y;
		}

		public void GoTo(Vector2 movePos, Vector2 facing)
		{
			this.GotoStep = 0;
			if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				this.HasPriorityOrder = true;
			}
			this.MovePosition.X = movePos.X;
			this.MovePosition.Y = movePos.Y;
			this.FinalFacingVector = facing;
			this.State = AIState.MoveTo;
		}

		public void HoldPosition()
		{

                if (this.Owner.isSpooling || this.Owner.engineState == Ship.MoveState.Warp)
                {
                    this.Owner.HyperspaceReturn();
                }
                this.State = AIState.HoldPosition;
                this.Owner.isThrusting = false;
 
		}

		private void MakeFinalApproach(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
		{
            if (Goal.TargetPlanet != null)
            {
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Last().Equals(Goal.TargetPlanet.Position);
                    Goal.MovePosition = Goal.TargetPlanet.Position;
                }
            }
            //if (this.RotateToFaceMovePosition(elapsedTime, Goal.MovePosition))
            //{
            //    Goal.SpeedLimit *= .9f;
            //}
            //else
            //{
            //    Goal.SpeedLimit *= 1.1f;
            //    if (this.Owner.engineState == Ship.MoveState.Sublight)
            //    {
            //        if (Goal.SpeedLimit > this.Owner.GetSTLSpeed())
            //            Goal.SpeedLimit = this.Owner.GetSTLSpeed();
            //    }
            //    else if (Goal.SpeedLimit > this.Owner.GetFTLSpeed())
            //        Goal.SpeedLimit = this.Owner.GetFTLSpeed();
            //}
            this.Owner.HyperspaceReturn();
			Vector2 velocity = this.Owner.Velocity;
            if (Goal.TargetPlanet != null)
                velocity += Goal.TargetPlanet.Position;
			float timetostop = velocity.Length() / Goal.SpeedLimit;
			float Distance = Vector2.Distance(this.Owner.Center, Goal.MovePosition);
			if (Distance / (Goal.SpeedLimit + 0.001f) <= timetostop)
			{
				this.OrderQueue.RemoveFirst();
			}
			else
			{
                if (DistanceLast == Distance)
                    Goal.SpeedLimit++;
                this.ThrustTowardsPosition(Goal.MovePosition, elapsedTime, Goal.SpeedLimit);
			}
			this.DistanceLast = Distance;
		}
        //added by gremlin Deveksmod MakeFinalApproach
        private void MakeFinalApproachDev(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
        {
            float speedLimit = (int)Goal.SpeedLimit;

            this.Owner.HyperspaceReturn();
            Vector2 velocity = this.Owner.Velocity;
            float Distance = Vector2.Distance(this.Owner.Center, Goal.MovePosition);
            double timetostop;

            timetostop = (double)velocity.Length() / speedLimit;

            //if(this.RotateToFaceMovePosition(elapsedTime, Goal))
            //{
            //    speedLimit--;
            //}
            //else
            //{
            //    speedLimit++;
            //    if(speedLimit > this.Owner.GetSTLSpeed())
            //        speedLimit=this.Owner.GetSTLSpeed();
            //}
            

            
            //ShipGoal preserveGoal = this.OrderQueue.Last();

            //if ((preserveGoal.TargetPlanet != null && this.Owner.fleet == null && Vector2.Distance(preserveGoal.TargetPlanet.Position, this.Owner.Center) > 7500) || this.DistanceLast == Distance)
            //{

            //    this.OrderQueue.Clear();
            //    this.OrderQueue.AddFirst(preserveGoal);
            //    return;
            //}

            if ((double)Distance / velocity.Length() <= timetostop)  //+ .005f) //(Distance  / (velocity.Length() ) <= timetostop)//
            {
                this.OrderQueue.RemoveFirst();
            }
            else
            {
                Goal.SpeedLimit = speedLimit;

                this.ThrustTowardsPosition(Goal.MovePosition, elapsedTime, speedLimit);
            }
            this.DistanceLast = Distance;
        }
		private void MakeFinalApproachFleet(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
		{
			float Distance = Vector2.Distance(this.Owner.Center, Goal.fleet.Position + this.Owner.FleetOffset);
			if (Distance < 100f || this.DistanceLast > Distance)
			{
				this.OrderQueue.RemoveFirst();
			}
			else
			{
				this.MoveTowardsPosition(Goal.fleet.Position + this.Owner.FleetOffset, elapsedTime, Goal.fleet.speed);
			}
			this.DistanceLast = Distance;
		}

		private void MoveInDirection(Vector2 direction, float elapsedTime)
		{
			if (!this.Owner.EnginesKnockedOut)
			{
				this.Owner.isThrusting = true;
				Vector2 wantedForward = Vector2.Normalize(direction);
				Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
				Vector2 right = new Vector2(-forward.Y, forward.X);
				float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
				float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
				if (angleDiff > 0.22f)
				{
					this.Owner.isTurning = true;
					float RotAmount = Math.Min(angleDiff, facing * elapsedTime * this.Owner.rotationRadiansPerSecond);
					if (Math.Abs(RotAmount) > angleDiff)
					{
						RotAmount = (RotAmount <= 0f ? -angleDiff : angleDiff);
					}
					if (RotAmount > 0f)
					{
						if (this.Owner.yRotation > -this.Owner.maxBank)
						{
							Ship owner = this.Owner;
							owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
						}
					}
					else if (RotAmount < 0f && this.Owner.yRotation < this.Owner.maxBank)
					{
						Ship ship = this.Owner;
						ship.yRotation = ship.yRotation + this.Owner.yBankAmount;
					}
					Ship rotation = this.Owner;
					rotation.Rotation = rotation.Rotation + RotAmount;
				}
				else if (this.Owner.yRotation > 0f)
				{
					Ship owner1 = this.Owner;
					owner1.yRotation = owner1.yRotation - this.Owner.yBankAmount;
					if (this.Owner.yRotation < 0f)
					{
						this.Owner.yRotation = 0f;
					}
				}
				else if (this.Owner.yRotation < 0f)
				{
					Ship ship1 = this.Owner;
					ship1.yRotation = ship1.yRotation + this.Owner.yBankAmount;
					if (this.Owner.yRotation > 0f)
					{
						this.Owner.yRotation = 0f;
					}
				}
				Ship velocity = this.Owner;
				velocity.Velocity = velocity.Velocity + (Vector2.Normalize(forward) * (elapsedTime * this.Owner.speed));
				if (this.Owner.Velocity.Length() > this.Owner.velocityMaximum)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * this.Owner.velocityMaximum;
				}
			}
		}

		private void MoveInDirectionAtSpeed(Vector2 direction, float elapsedTime, float speed)
		{
			if (speed == 0f)
			{
				this.Owner.isThrusting = false;
				this.Owner.Velocity = Vector2.Zero;
				return;
			}
			if (!this.Owner.EnginesKnockedOut)
			{
				this.Owner.isThrusting = true;
				Vector2 wantedForward = Vector2.Normalize(direction);
				Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
				Vector2 right = new Vector2(-forward.Y, forward.X);
				float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
				float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
				if (angleDiff <= 0.02f)
				{
					this.DeRotate();
				}
				else
				{
					this.Owner.isTurning = true;
					Ship owner = this.Owner;
					owner.Rotation = owner.Rotation + Math.Min(angleDiff, facing * elapsedTime * this.Owner.rotationRadiansPerSecond);
				}
				Ship velocity = this.Owner;
				velocity.Velocity = velocity.Velocity + (Vector2.Normalize(forward) * (elapsedTime * speed));
				if (this.Owner.Velocity.Length() > speed)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * speed;
				}
			}
		}

		private void MoveTowardsPosition(Vector2 Position, float elapsedTime)
		{
			if (Vector2.Distance(this.Owner.Center, Position) < 50f)
			{
				this.Owner.Velocity = Vector2.Zero;
				return;
			}
			Position = Position - this.Owner.Velocity;
			if (!this.Owner.EnginesKnockedOut)
			{
				this.Owner.isThrusting = true;
				Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, Position));
				Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
				Vector2 right = new Vector2(-forward.Y, forward.X);
				float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
				float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
				if (angleDiff > 0.02f)
				{
					float RotAmount = Math.Min(angleDiff, facing * elapsedTime * this.Owner.rotationRadiansPerSecond);
					if (RotAmount > 0f)
					{
						if (this.Owner.yRotation > -this.Owner.maxBank)
						{
							Ship owner = this.Owner;
							owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
						}
					}
					else if (RotAmount < 0f && this.Owner.yRotation < this.Owner.maxBank)
					{
						Ship ship = this.Owner;
						ship.yRotation = ship.yRotation + this.Owner.yBankAmount;
					}
					this.Owner.isTurning = true;
					Ship rotation = this.Owner;
					rotation.Rotation = rotation.Rotation + RotAmount;
				}
				float speedLimit = this.Owner.speed;
				if (this.Owner.isSpooling)
				{
					speedLimit = speedLimit * this.Owner.loyalty.data.FTLModifier;
				}
				else if (Vector2.Distance(Position, this.Owner.Center) < speedLimit)
				{
					speedLimit = Vector2.Distance(Position, this.Owner.Center) * 0.75f;
				}
				Ship velocity = this.Owner;
				velocity.Velocity = velocity.Velocity + (Vector2.Normalize(forward) * (elapsedTime * speedLimit));
				if (this.Owner.Velocity.Length() > speedLimit)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * speedLimit;
				}
			}
		}

		private void MoveTowardsPosition(Vector2 Position, float elapsedTime, float speedLimit)
		{
			if (speedLimit == 0f)
			{
				speedLimit = 200f;
			}
			Position = Position - this.Owner.Velocity;
			if (!this.Owner.EnginesKnockedOut)
			{
				this.Owner.isThrusting = true;
				Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, Position));
				Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
				Vector2 right = new Vector2(-forward.Y, forward.X);
				float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
				float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
				if (angleDiff > 0.02f)
				{
					float RotAmount = Math.Min(angleDiff, facing * elapsedTime * this.Owner.rotationRadiansPerSecond);
					if (RotAmount > 0f)
					{
						if (this.Owner.yRotation > -this.Owner.maxBank)
						{
							Ship owner = this.Owner;
							owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
						}
					}
					else if (RotAmount < 0f && this.Owner.yRotation < this.Owner.maxBank)
					{
						Ship ship = this.Owner;
						ship.yRotation = ship.yRotation + this.Owner.yBankAmount;
					}
					this.Owner.isTurning = true;
					Ship rotation = this.Owner;
					rotation.Rotation = rotation.Rotation + RotAmount;
				}
				if (this.Owner.isSpooling)
				{
					speedLimit = speedLimit * this.Owner.loyalty.data.FTLModifier;
				}
				Ship velocity = this.Owner;
				velocity.Velocity = velocity.Velocity + (Vector2.Normalize(forward) * (elapsedTime * speedLimit));
				if (this.Owner.Velocity.Length() > speedLimit)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * speedLimit;
				}
			}
		}

		private void MoveToWithin1000(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
        {

            if (this.OrderQueue.Count > 1 && this.OrderQueue.Skip(1).First().Plan != Plan.MoveToWithin1000 && goal.TargetPlanet != null)
            {
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Last().Equals(goal.TargetPlanet.Position);
                    goal.MovePosition = goal.TargetPlanet.Position;
                }
            }
            float speedLimit =  (int)(this.Owner.speed)  ;
            float single = Vector2.Distance(this.Owner.Center, goal.MovePosition);
            if (this.ActiveWayPoints.Count <= 1)
            {
                if (single  < this.Owner.speed)
                {
                    speedLimit = single;
                    //this.Owner.speed =this.Owner.speed < single ? this.Owner.speed: single;
                }
      
            }
            this.ThrustTowardsPosition(goal.MovePosition, elapsedTime, speedLimit);
            if (this.ActiveWayPoints.Count <= 1)
            {
                if (single <= 1500f)
                {
                    lock (this.wayPointLocker)
                    {
                        if (this.ActiveWayPoints.Count > 1)
                        {
                            this.ActiveWayPoints.Dequeue();
                        }
                        if (this.OrderQueue.Count > 0)
                        {
                            this.OrderQueue.RemoveFirst();
                        }
                    }
                }
                //else if(this.ColonizeTarget !=null)
                //{
                //    lock (this.wayPointLocker)
                //    {
                //        this.ActiveWayPoints.First().Equals(this.ColonizeTarget.Position);
                //        this.OrderQueue.First().MovePosition = this.ColonizeTarget.Position;
                //    }
                //}
                

            }
            else if (this.Owner.engineState == Ship.MoveState.Warp)
            {
                if (single <= 15000f)
                {
                    lock (this.wayPointLocker)
                    {
                        this.ActiveWayPoints.Dequeue();
                        if (this.OrderQueue.Count > 0)
                        {
                            this.OrderQueue.RemoveFirst();
                        }
                    }
                }
                //if (this.ColonizeTarget != null )
                //{
                //    lock (this.wayPointLocker)
                //    {

                //        if (this.OrderQueue.Where(cgoal => cgoal.Plan == Plan.MoveToWithin1000).Count() == 1)
                //        {
                //            this.ActiveWayPoints.First().Equals(this.ColonizeTarget.Position);
                //            this.OrderQueue.First().MovePosition = this.ColonizeTarget.Position;
                //        }
                //    }
                //}
            }
            else if (single <= 1500f)
            {
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Dequeue();
                    if (this.OrderQueue.Count > 0)
                    {
                        this.OrderQueue.RemoveFirst();
                    }
                }
            }
            //else if (this.ColonizeTarget != null)
            //{
            //    lock (this.wayPointLocker)
            //    {
            //        this.ActiveWayPoints.First().Equals(this.ColonizeTarget.Position);
            //    }
            //}
        }

		private void MoveToWithin1000Fleet(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
			float Distance = Vector2.Distance(this.Owner.Center, goal.fleet.Position + this.Owner.FleetOffset);
            float speedLimit = goal.SpeedLimit;
            if (this.Owner.velocityMaximum >= Distance)
            {
                speedLimit = Distance;
            }
            
            if (Distance > 10000f)
			{
				this.Owner.EngageStarDrive();
			}
			else if (Distance < 1000f)
			{
				this.Owner.HyperspaceReturn();
				this.OrderQueue.RemoveFirst();
				return;
			}
            this.MoveTowardsPosition(goal.fleet.Position + this.Owner.FleetOffset, elapsedTime, speedLimit);
		}

		private void OrbitShip(Ship ship, float elapsedTime)
		{
			this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, ship.Center, 1500f);
			if (Vector2.Distance(this.OrbitPos, this.Owner.Center) < 1500f)
			{
				ArtificialIntelligence orbitalAngle = this;
				orbitalAngle.OrbitalAngle = orbitalAngle.OrbitalAngle + 15f;
				if (this.OrbitalAngle >= 360f)
				{
					ArtificialIntelligence artificialIntelligence = this;
					artificialIntelligence.OrbitalAngle = artificialIntelligence.OrbitalAngle - 360f;
				}
				this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, ship.Position, 2500f);
			}
			this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
		}

		private void OrbitShipLeft(Ship ship, float elapsedTime)
		{
			this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, ship.Center, 1500f);
			if (Vector2.Distance(this.OrbitPos, this.Owner.Center) < 1500f)
			{
				ArtificialIntelligence orbitalAngle = this;
				orbitalAngle.OrbitalAngle = orbitalAngle.OrbitalAngle - 15f;
				if (this.OrbitalAngle >= 360f)
				{
					ArtificialIntelligence artificialIntelligence = this;
					artificialIntelligence.OrbitalAngle = artificialIntelligence.OrbitalAngle - 360f;
				}
				this.OrbitPos = this.GeneratePointOnCircle(this.OrbitalAngle, ship.Position, 2500f);
			}
			this.ThrustTowardsPosition(this.OrbitPos, elapsedTime, this.Owner.speed);
		}

		public void OrderAllStop()
		{
			this.OrderQueue.Clear();
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.State = AIState.HoldPosition;
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateInlineWithVelocity, Vector2.Zero, 0f));
			ArtificialIntelligence.ShipGoal stop = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Stop, Vector2.Zero, 0f);
			this.OrderQueue.AddLast(stop);
		}

		public void OrderAttackSpecificTarget(Ship toAttack)
		{
			this.TargetQueue.Clear();
            
			if (toAttack == null)
			{
				return;
			}

			if (this.Owner.loyalty.GetRelations().ContainsKey(toAttack.loyalty))
			{
				if (!this.Owner.loyalty.GetRelations()[toAttack.loyalty].Treaty_Peace)
				{
					if (this.State == AIState.AttackTarget && this.Target == toAttack)
					{
						return;
					}
					if (this.State == AIState.SystemDefender && this.Target == toAttack)
					{
						return;
					}
					if (this.Owner.Weapons.Count == 0 || this.Owner.Role == "troop")
					{
						this.OrderInterceptShip(toAttack);
						return;
					}
					this.Intercepting = true;
					lock (this.wayPointLocker)
					{
						this.ActiveWayPoints.Clear();
					}
					this.State = AIState.AttackTarget;
					this.Target = toAttack;
					this.Owner.InCombatTimer = 15f;
					this.OrderQueue.Clear();
					this.IgnoreCombat = false;
					this.TargetQueue.Add(toAttack);
					this.hasPriorityTarget = true;
					this.HasPriorityOrder = false;
					ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
					this.OrderQueue.AddLast(combat);
					return;
				}
				this.OrderInterceptShip(toAttack);
			}
		}

        /**
            This method orders this ship to bombard the specified planet.
            @param toBombard - the planet that this ship is directed to bombard.
        */
        public void OrderBombardPlanet(Planet toBombard)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.State = AIState.Bombard;
			this.Owner.InCombatTimer = 15f;
			this.OrderQueue.Clear(); //rescind any current directives before adding "bombard" directive to order queue
			this.HasPriorityOrder = true;
			ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Bombard, Vector2.Zero, 0f)
			{
				TargetPlanet = toBombard
			};
			this.OrderQueue.AddLast(combat);
		}
        /**
           This method orders this ship to bombard the specified planet
           @param toBombard - the planet that this ship is directed to bombard.
           
       */
        public void OrderBombardTroops(Planet toBombard)
        {
            lock (this.wayPointLocker)
            {
                this.ActiveWayPoints.Clear();
            }
            this.State = AIState.BombardTroops;
            this.Owner.InCombatTimer = 15f;
            this.OrderQueue.Clear();
            this.HasPriorityOrder = true;
            ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.BombTroops, Vector2.Zero, 0f)
            {
                TargetPlanet = toBombard
            };
            this.OrderQueue.AddLast(combat);
        }

		public void OrderColonization(Planet toColonize)
		{
			if (toColonize == null)
			{
				return;
			}
			this.ColonizeTarget = toColonize;
			this.OrderMoveTowardsPosition(toColonize.Position, 0f, new Vector2(0f, -1f), true, toColonize);
            ArtificialIntelligence.ShipGoal colonize = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Colonize, toColonize.Position, 0f)
			{
				TargetPlanet = this.ColonizeTarget
			};
			this.OrderQueue.AddLast(colonize);
			this.State = AIState.Colonize;
		}

		public void OrderDeepSpaceBuild(Goal goal)
		{
			this.OrderQueue.Clear();
			this.OrderMoveTowardsPosition(goal.BuildPosition, MathHelper.ToRadians(HelperFunctions.findAngleToTarget(this.Owner.Center, goal.BuildPosition)), this.findVectorToTarget(this.Owner.Center, goal.BuildPosition), true,null);
			ArtificialIntelligence.ShipGoal Deploy = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DeployStructure, goal.BuildPosition, MathHelper.ToRadians(HelperFunctions.findAngleToTarget(this.Owner.Center, goal.BuildPosition)))
			{
				goal = goal,
				VariableString = goal.ToBuildUID
			};
			this.OrderQueue.AddLast(Deploy);
		}

		public void OrderExplore()
		{
			if (this.State == AIState.Explore && this.ExplorationTarget != null)
			{
				return;
			}
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.OrderQueue.Clear();
			this.State = AIState.Explore;
			ArtificialIntelligence.ShipGoal Explore = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Explore, Vector2.Zero, 0f);
			this.OrderQueue.AddLast(Explore);
		}

		public void OrderExterminatePlanet(Planet toBombard)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.State = AIState.Exterminate;
			this.OrderQueue.Clear();
			ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Exterminate, Vector2.Zero, 0f)
			{
				TargetPlanet = toBombard
			};
			this.OrderQueue.AddLast(combat);
		}

		public void OrderFindExterminationTarget(bool ClearOrders)
		{
			if (this.ExterminationTarget == null || this.ExterminationTarget.Owner == null)
			{
				List<Planet> plist = new List<Planet>();
				foreach (KeyValuePair<Guid, Planet> planetsDict in ArtificialIntelligence.universeScreen.PlanetsDict)
				{
					if (planetsDict.Value.Owner == null)
					{
						continue;
					}
					plist.Add(planetsDict.Value);
				}
				IOrderedEnumerable<Planet> sortedList = 
					from planet in plist
					orderby Vector2.Distance(this.Owner.Center, planet.Position)
					select planet;
				if (sortedList.Count<Planet>() > 0)
				{
					this.ExterminationTarget = sortedList.First<Planet>();
					this.OrderExterminatePlanet(this.ExterminationTarget);
					return;
				}
			}
			else if (this.ExterminationTarget != null && this.OrderQueue.Count == 0)
			{
				this.OrderExterminatePlanet(this.ExterminationTarget);
			}
		}

		public void OrderFormationWarp(Vector2 destination, float facing, Vector2 fvec)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.OrderQueue.Clear();
			this.OrderMoveDirectlyTowardsPosition(destination, facing, fvec, true, this.Owner.fleet.speed);
			this.State = AIState.FormationWarp;
		}

		public void OrderFormationWarpQ(Vector2 destination, float facing, Vector2 fvec)
		{
			this.OrderMoveDirectlyTowardsPosition(destination, facing, fvec, false, this.Owner.fleet.speed);
			this.State = AIState.FormationWarp;
		}

		public void OrderInterceptShip(Ship toIntercept)
		{
			this.Intercepting = true;
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.State = AIState.Intercept;
			this.Target = toIntercept;
			this.hasPriorityTarget = true;
			this.HasPriorityOrder = false;
			this.OrderQueue.Clear();
		}

		public void OrderLandAllTroops(Planet target)
		{
            if ((this.Owner.Role == "troop" || this.Owner.HasTroopBay || this.Owner.hasTransporter) &&  this.Owner.TroopList.Count > 0  && target.GetGroundLandingSpots() >0 )
            {
                this.HasPriorityOrder = true;
                this.State = AIState.AssaultPlanet;
                this.OrbitTarget = target;
                this.OrderQueue.Clear();
                ArtificialIntelligence.ShipGoal goal = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.LandTroop, Vector2.Zero, 0f)
                {
                    TargetPlanet = target
                };
                this.OrderQueue.AddLast(goal);
            }
            else if (this.Owner.BombBays.Count > 0 && target.GetGroundStrength(this.Owner.loyalty) ==0)  //universeScreen.player == this.Owner.loyalty && 
            {
                this.State = AIState.Bombard;
                this.OrderBombardTroops(target);
            }
		}

		public void OrderMoveDirectlyTowardsPosition(Vector2 position, float desiredFacing, Vector2 fVec, bool ClearOrders)
		{
			this.Target = null;
			this.hasPriorityTarget = false;
			Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, position));
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
			Vector2.Dot(wantedForward, right);
			if (angleDiff > 0.2f)
			{
				this.Owner.HyperspaceReturn();
			}
			this.OrderQueue.Clear();
			if (ClearOrders)
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				this.HasPriorityOrder = true;
			}
			this.State = AIState.MoveTo;
			this.MovePosition = position;
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Enqueue(position);
			}
			this.FinalFacingVector = fVec;
			this.DesiredFacing = desiredFacing;
			lock (this.wayPointLocker)
			{
				for (int i = 0; i < this.ActiveWayPoints.Count; i++)
				{
					Vector2 waypoint = this.ActiveWayPoints.ToArray()[i];
					if (i != 0)
					{
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(to1k);
					}
					else
					{
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToFaceMovePosition, waypoint, 0f));
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(to1k);
					}
					if (i == this.ActiveWayPoints.Count - 1)
					{
						ArtificialIntelligence.ShipGoal finalApproach = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproach, waypoint, desiredFacing)
						{
							SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(finalApproach);
						ArtificialIntelligence.ShipGoal slow = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.StopWithBackThrust, waypoint, 0f)
						{
							SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(slow);
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToDesiredFacing, waypoint, desiredFacing));
					}
				}
			}
		}

		public void OrderMoveDirectlyTowardsPosition(Vector2 position, float desiredFacing, Vector2 fVec, bool ClearOrders, float speedLimit)
		{
			this.Target = null;
			this.hasPriorityTarget = false;
			Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, position));
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
			Vector2.Dot(wantedForward, right);
			if (angleDiff > 0.2f)
			{
				this.Owner.HyperspaceReturn();
			}
			this.OrderQueue.Clear();
			if (ClearOrders)
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				this.HasPriorityOrder = true;
			}
			this.State = AIState.MoveTo;
			this.MovePosition = position;
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Enqueue(position);
			}
			this.FinalFacingVector = fVec;
			this.DesiredFacing = desiredFacing;
			lock (this.wayPointLocker)
			{
				for (int i = 0; i < this.ActiveWayPoints.Count; i++)
				{
					Vector2 waypoint = this.ActiveWayPoints.ToArray()[i];
					if (i != 0)
					{
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							SpeedLimit = speedLimit
						};
						this.OrderQueue.AddLast(to1k);
					}
					else
					{
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToFaceMovePosition, waypoint, 0f));
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							SpeedLimit = speedLimit
						};
						this.OrderQueue.AddLast(to1k);
					}
					if (i == this.ActiveWayPoints.Count - 1)
					{
						ArtificialIntelligence.ShipGoal finalApproach = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproach, waypoint, desiredFacing)
						{
							SpeedLimit = speedLimit
						};
						this.OrderQueue.AddLast(finalApproach);
						ArtificialIntelligence.ShipGoal slow = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.StopWithBackThrust, waypoint, 0f)
						{
							SpeedLimit = speedLimit
						};
						this.OrderQueue.AddLast(slow);
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToDesiredFacing, waypoint, desiredFacing));
					}
				}
			}
		}

		public void OrderMoveToFleetPosition(Vector2 position, float desiredFacing, Vector2 fVec, bool ClearOrders, float SpeedLimit, Fleet fleet)
		{
			SpeedLimit = this.Owner.speed;
			if (ClearOrders)
			{
				this.OrderQueue.Clear();
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			this.State = AIState.MoveTo;
			this.MovePosition = position;
			this.FinalFacingVector = fVec;
			this.DesiredFacing = desiredFacing;
			bool inCombat = this.Owner.InCombat;
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToFaceMovePosition, this.MovePosition, 0f));
			ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000Fleet, this.MovePosition, desiredFacing)
			{
				SpeedLimit = SpeedLimit,
				fleet = fleet
			};
			this.OrderQueue.AddLast(to1k);
			ArtificialIntelligence.ShipGoal finalApproach = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproachFleet, this.MovePosition, desiredFacing)
			{
				SpeedLimit = SpeedLimit,
				fleet = fleet
			};
			this.OrderQueue.AddLast(finalApproach);
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateInlineWithVelocity, Vector2.Zero, 0f));
			ArtificialIntelligence.ShipGoal slow = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.StopWithBackThrust, position, 0f)
			{
				SpeedLimit = this.Owner.speed
			};
			this.OrderQueue.AddLast(slow);
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToDesiredFacing, this.MovePosition, desiredFacing));
		}

		public void OrderMoveTowardsPosition( Vector2  position , float desiredFacing, Vector2 fVec, bool ClearOrders, Planet TargetPlanet)
		{
            this.DistanceLast = 0f;
            this.Target = null;
			this.hasPriorityTarget = false;
			Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, position));
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
			Vector2.Dot(wantedForward, right);
			if (angleDiff > 0.2f)
			{
				this.Owner.HyperspaceReturn();
			}
			this.OrderQueue.Clear();
			if (ClearOrders)
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			if (ArtificialIntelligence.universeScreen != null && this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				this.HasPriorityOrder = true;
			}
			this.State = AIState.MoveTo;
			this.MovePosition = position;
			try
			{
				this.PlotCourseToNew(position, (this.ActiveWayPoints.Count > 0 ? this.ActiveWayPoints.Last<Vector2>() : this.Owner.Center));
			}
			catch
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			this.FinalFacingVector = fVec;
			this.DesiredFacing = desiredFacing;

			lock (this.wayPointLocker)
			{
                            Planet p;
            Vector2 waypoint;
                int AWPC = this.ActiveWayPoints.Count;
                for (int i = 0; i < AWPC; i++)
				{
					p =null;
                    waypoint = this.ActiveWayPoints.ToArray()[i];
					if (i != 0)
					{
                        if (AWPC - 1 == i)
                            p = TargetPlanet;

                        ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							TargetPlanet=p,
                            SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(to1k);
					}
					else
					{
						if(AWPC -1 ==i)
                            p = TargetPlanet;
                        this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToFaceMovePosition, waypoint, 0f));
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
                            TargetPlanet = p,
                            SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(to1k);
					}
                    if (i == AWPC - 1)
					{
						ArtificialIntelligence.ShipGoal finalApproach = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproach, waypoint, desiredFacing)
						{
							TargetPlanet=p,
                            SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(finalApproach);
						ArtificialIntelligence.ShipGoal slow = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.StopWithBackThrust, waypoint, 0f)
						{
                            TargetPlanet = TargetPlanet,
                            SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(slow);
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToDesiredFacing, waypoint, desiredFacing));
					}
				}
			}
		}

        #region Unreferenced code
        //public void OrderMoveTowardsPosition(Vector2 position, float desiredFacing, Vector2 fVec, bool ClearOrders, float SpeedLimit)
        //{
        //    this.Target = null;
        //    Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, position));
        //    Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
        //    Vector2 right = new Vector2(-forward.Y, forward.X);
        //    float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
        //    Vector2.Dot(wantedForward, right);
        //    if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
        //    {
        //        this.HasPriorityOrder = true;
        //    }
        //    if (angleDiff > 0.2f)
        //    {
        //        this.Owner.HyperspaceReturn();
        //    }
        //    this.hasPriorityTarget = false;
        //    if (ClearOrders)
        //    {
        //        this.OrderQueue.Clear();
        //    }
        //    this.State = AIState.MoveTo;
        //    this.MovePosition = position;
        //    this.PlotCourseToNew(position, this.Owner.Center);
        //    this.FinalFacingVector = fVec;
        //    this.DesiredFacing = desiredFacing;
        //    for (int i = 0; i < this.ActiveWayPoints.Count; i++)
        //    {
        //        Vector2 waypoint = this.ActiveWayPoints.ToArray()[i];
        //        if (i != 0)
        //        {
        //            ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
        //            {
        //                SpeedLimit = SpeedLimit
        //            };
        //            this.OrderQueue.AddLast(to1k);
        //        }
        //        else
        //        {
        //            ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
        //            {
        //                SpeedLimit = SpeedLimit
        //            };
        //            this.OrderQueue.AddLast(to1k);
        //        }
        //        if (i == this.ActiveWayPoints.Count - 1)
        //        {
        //            ArtificialIntelligence.ShipGoal finalApproach = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproach, waypoint, desiredFacing)
        //            {
        //                SpeedLimit = SpeedLimit
        //            };
        //            this.OrderQueue.AddLast(finalApproach);
        //            this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateInlineWithVelocity, Vector2.Zero, 0f));
        //            ArtificialIntelligence.ShipGoal slow = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.StopWithBackThrust, waypoint, 0f)
        //            {
        //                SpeedLimit = SpeedLimit
        //            };
        //            this.OrderQueue.AddLast(slow);
        //            this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToDesiredFacing, waypoint, desiredFacing));
        //        }
        //    }
        //} 
        #endregion

		public void OrderOrbitNearest(bool ClearOrders)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.Target = null;
			this.Intercepting = false;
			this.Owner.HyperspaceReturn();
			if (ClearOrders)
			{
				this.OrderQueue.Clear();
			}
			IOrderedEnumerable<Planet> sortedList = 
				from toOrbit in this.Owner.loyalty.GetPlanets()
				orderby Vector2.Distance(this.Owner.Center, toOrbit.Position)
				select toOrbit;
			if (sortedList.Count<Planet>() > 0)
			{
				Planet planet = sortedList.First<Planet>();
				this.OrbitTarget = planet;
				ArtificialIntelligence.ShipGoal orbit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Orbit, Vector2.Zero, 0f)
				{
					TargetPlanet = planet
				};
				this.resupplyTarget = planet;
				this.OrderQueue.AddLast(orbit);
				this.State = AIState.Orbit;
				return;
			}
			IOrderedEnumerable<SolarSystem> systemList = 
				from solarsystem in this.Owner.loyalty.GetOwnedSystems()
				orderby Vector2.Distance(this.Owner.Center, solarsystem.Position)
				select solarsystem;
			if (systemList.Count<SolarSystem>() > 0)
			{
				Planet item = systemList.First<SolarSystem>().PlanetList[0];
				this.OrbitTarget = item;
				ArtificialIntelligence.ShipGoal orbit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Orbit, Vector2.Zero, 0f)
				{
					TargetPlanet = item
				};
				this.resupplyTarget = item;
				this.OrderQueue.AddLast(orbit);
				this.State = AIState.Orbit;
			}
		}
        //added by gremlin to run away
        public void OrderFlee(bool ClearOrders)
        {
            lock (this.wayPointLocker)
            {
                this.ActiveWayPoints.Clear();
            }
            this.Target = null;
            this.Intercepting = false;
            this.Owner.HyperspaceReturn();
            if (ClearOrders)
            {
                this.OrderQueue.Clear();
            }
           
            IOrderedEnumerable<SolarSystem> systemList =
                from solarsystem in this.Owner.loyalty.GetOwnedSystems()
                where solarsystem.combatTimer <=0 && Vector2.Distance(solarsystem.Position,this.Owner.Position) >300000
                orderby Vector2.Distance(this.Owner.Center, solarsystem.Position)
                select solarsystem;
            if (systemList.Count<SolarSystem>() > 0)
            {
                Planet item = systemList.First<SolarSystem>().PlanetList[0];
                this.OrbitTarget = item;
                ArtificialIntelligence.ShipGoal orbit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Orbit, Vector2.Zero, 0f)
                {
                    TargetPlanet = item
                };
                this.resupplyTarget = item;
                this.OrderQueue.AddLast(orbit);
                this.State = AIState.Flee;
            }
        }

		public void OrderOrbitPlanet(Planet p)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.Target = null;
			this.Intercepting = false;
			this.Owner.HyperspaceReturn();
			this.OrbitTarget = p;
			this.OrderQueue.Clear();
			ArtificialIntelligence.ShipGoal orbit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Orbit, Vector2.Zero, 0f)
			{
				TargetPlanet = p
			};
			this.resupplyTarget = p;
			this.OrderQueue.AddLast(orbit);
			this.State = AIState.Orbit;
		}

		public void OrderQueueSpecificTarget(Ship toAttack)
		{
			if (this.TargetQueue.Count == 0 && this.Target != null && this.Target.Active && this.Target != toAttack)
			{
				this.OrderAttackSpecificTarget(this.Target as Ship);
				this.TargetQueue.Add(this.Target as Ship);
			}
			if (this.TargetQueue.Count == 0)
			{
				this.OrderAttackSpecificTarget(toAttack);
				return;
			}
			if (toAttack == null)
			{
				return;
			}
			if (this.Owner.loyalty.GetRelations().ContainsKey(toAttack.loyalty))
			{
				if (!this.Owner.loyalty.GetRelations()[toAttack.loyalty].Treaty_Peace)
				{
					if (this.State == AIState.AttackTarget && this.Target == toAttack)
					{
						return;
					}
					if (this.State == AIState.SystemDefender && this.Target == toAttack)
					{
						return;
					}
					if (this.Owner.Weapons.Count == 0 || this.Owner.Role == "troop")
					{
						this.OrderInterceptShip(toAttack);
						return;
					}
					this.Intercepting = true;
					lock (this.wayPointLocker)
					{
						this.ActiveWayPoints.Clear();
					}
					this.State = AIState.AttackTarget;
					this.TargetQueue.Add(toAttack);
					this.hasPriorityTarget = true;
					this.HasPriorityOrder = false;
					return;
				}
				this.OrderInterceptShip(toAttack);
			}
		}

        public void OrderRebase(Planet p, bool ClearOrders)
        {

            lock (this.wayPointLocker)
            {
                this.ActiveWayPoints.Clear();
            }
            if (ClearOrders)
            {
                this.OrderQueue.Clear();
            }
            int troops = this.Owner.loyalty.GetShips()
    .Where(troop => troop.TroopList.Count > 0)
    .Where(troopAI => troopAI.GetAI().OrderQueue
        .Where(goal => goal.TargetPlanet != null && goal.TargetPlanet == p).Count() > 0).Count();

            if (troops >= p.GetGroundLandingSpots())
            {
                this.OrderQueue.Clear();
                this.State = AIState.AwaitingOrders;
                return;
            }

            this.OrderMoveTowardsPosition(p.Position, 0f, new Vector2(0f, -1f), false,p);
            this.IgnoreCombat = true;
            ArtificialIntelligence.ShipGoal rebase = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Rebase, Vector2.Zero, 0f)
            {
                TargetPlanet = p
            };
            this.OrderQueue.AddLast(rebase);
            this.State = AIState.Rebase;
            this.HasPriorityOrder = true;
        }

		public void OrderRebaseToNearest()
		{
            ////added by gremlin if rebasing dont rebase.
            //if (this.State == AIState.Rebase && this.OrbitTarget.Owner == this.Owner.loyalty)
            //    return;
            lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
            
            IOrderedEnumerable<Planet> sortedList = 
				from planet in this.Owner.loyalty.GetPlanets()
                //added by gremlin if the planet is full of troops dont rebase there. RERC2 I dont think the about looking at incoming troops works.
                where this.Owner.loyalty.GetShips()
    .Where(troop => troop.TroopList.Count > 0 )
    .Where(troopAI => troopAI.GetAI().OrderQueue
        .Where(goal => goal.TargetPlanet != null && goal.TargetPlanet == planet).Count() > 0).Count() <= planet.GetGroundLandingSpots()


                /*where planet.TroopsHere.Count + this.Owner.loyalty.GetShips()
                .Where(troop => troop.Role == "troop" 
                    
                    && troop.GetAI().State == AIState.Rebase 
                    && troop.GetAI().OrbitTarget == planet).Count() < planet.TilesList.Sum(space => space.number_allowed_troops)*/
				orderby Vector2.Distance(this.Owner.Center, planet.Position)
				select planet;

           


			if (sortedList.Count<Planet>() <= 0)
			{
				this.State = AIState.AwaitingOrders;
				return;
			}
			Planet p = sortedList.First<Planet>();
			this.OrderMoveTowardsPosition(p.Position, 0f, new Vector2(0f, -1f), false,p);
			this.IgnoreCombat = true;
			ArtificialIntelligence.ShipGoal rebase = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Rebase, Vector2.Zero, 0f)
			{
				TargetPlanet = p
			};

            this.orderqueue.EnterWriteLock();
            this.OrderQueue.AddLast(rebase);
            this.orderqueue.ExitWriteLock();
			this.State = AIState.Rebase;
			this.HasPriorityOrder = true;
		}

		public void OrderRefitTo(string toRefit)
		{
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.HasPriorityOrder = true;
			this.IgnoreCombat = true;
            this.orderqueue.EnterWriteLock();
			this.OrderQueue.Clear();
            this.orderqueue.ExitWriteLock();
			IOrderedEnumerable<Ship_Game.Planet> sortedList = 
				from planet in this.Owner.loyalty.GetPlanets()
				orderby Vector2.Distance(this.Owner.Center, planet.Position)
				select planet;
			this.OrbitTarget = null;
			foreach (Ship_Game.Planet Planet in sortedList)
			{
				if (!Planet.HasShipyard)
				{
					continue;
				}
				this.OrbitTarget = Planet;
				break;
			}
			if (this.OrbitTarget == null)
			{
				this.State = AIState.AwaitingOrders;
				return;
			}
			this.OrderMoveTowardsPosition(this.OrbitTarget.Position, 0f, Vector2.One, true,this.OrbitTarget);
			ArtificialIntelligence.ShipGoal refit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Refit, Vector2.Zero, 0f)
			{
				TargetPlanet = this.OrbitTarget,
				VariableString = toRefit
			};
			this.OrderQueue.AddLast(refit);
			this.State = AIState.Refit;
		}

		public void OrderResupply(Planet toOrbit, bool ClearOrders)
		{
			if (ClearOrders)
			{
				this.OrderQueue.Clear();
			}
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.Target = null;
			this.OrbitTarget = toOrbit;
			this.OrderMoveTowardsPosition(toOrbit.Position, 0f, Vector2.One, true,toOrbit);
			this.State = AIState.Resupply;
			this.HasPriorityOrder = true;
		}

		public void OrderResupplyNearest()
		{
			if (this.Owner.Mothership != null && this.Owner.Mothership.Active)
			{
				this.OrderReturnToHangar();
				return;
			}
			List<Planet> shipyards = new List<Planet>();
			foreach (Planet planet in this.Owner.loyalty.GetPlanets())
			{
				if (!planet.HasShipyard)
				{
					continue;
				}
				shipyards.Add(planet);
			}
			IOrderedEnumerable<Planet> sortedList = 
				from p in shipyards
				orderby Vector2.Distance(this.Owner.Center, p.Position)
				select p;
			if (sortedList.Count<Planet>() > 0)
			{
				this.OrderResupply(sortedList.First<Planet>(), true);
			}
		}

		public void OrderReturnToHangar()
		{
			ArtificialIntelligence.ShipGoal g = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.ReturnToHangar, Vector2.Zero, 0f);
            this.orderqueue.EnterWriteLock();
            this.OrderQueue.Clear();
			this.OrderQueue.AddLast(g);
            this.orderqueue.ExitWriteLock();
			this.HasPriorityOrder = true;
			this.State = AIState.ReturnToHangar;
		}

		public void OrderScrapShip()
		{
#if SHOWSCRUB
            System.Diagnostics.Debug.WriteLine(string.Concat(this.Owner.loyalty.PortraitName, " : ", this.Owner.Role)); 
#endif
           
            if((this.Owner.Role=="platform" || this.Owner.Role == "station" )&& this.Owner.ScuttleTimer<1)
            {
                this.Owner.ScuttleTimer = 1;
                this.State = AIState.Scuttle;
                this.HasPriorityOrder = true;
                return;
            }
            lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
            this.Owner.loyalty.ForcePoolRemove(this.Owner);
            
            if (this.Owner.fleet != null)
                this.Owner.fleet = null;
            this.HasPriorityOrder = true;
            this.IgnoreCombat = true;
			this.OrderQueue.Clear();
			IOrderedEnumerable<Ship_Game.Planet> sortedList = 
				from planet in this.Owner.loyalty.GetPlanets()
				orderby Vector2.Distance(this.Owner.Center, planet.Position)
				select planet;
			this.OrbitTarget = null;
			foreach (Ship_Game.Planet Planet in sortedList)
			{
				if (!Planet.HasShipyard)
				{
					continue;
				}
				this.OrbitTarget = Planet;
				break;
			}
			if (this.OrbitTarget == null)
			{
				this.State = AIState.AwaitingOrders;
			}
			else
			{
				this.OrderMoveTowardsPosition(this.OrbitTarget.Position, 0f, Vector2.One, true,this.OrbitTarget);
				ArtificialIntelligence.ShipGoal scrap = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Scrap, Vector2.Zero, 0f)
				{
					TargetPlanet = this.OrbitTarget
				};
				this.OrderQueue.AddLast(scrap);
				this.State = AIState.Scrap;
			}
			this.State = AIState.Scrap;
		}

		private void OrderSupplyShip(Ship tosupply, float ord_amt)
		{
			ArtificialIntelligence.ShipGoal g = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.SupplyShip, Vector2.Zero, 0f);
			this.EscortTarget = tosupply;
			g.VariableNumber = ord_amt;
			this.IgnoreCombat = true;
			this.OrderQueue.Clear();
			this.OrderQueue.AddLast(g);
			this.State = AIState.Ferrying;
		}

		public void OrderSystemDefense(SolarSystem system)
		{
            //if (this.State == AIState.Intercept || this.Owner.InCombatTimer > 0)
            //    return;
            //bool inSystem = true;
            //if (this.Owner.BaseCanWarp && Vector2.Distance(system.Position, this.Owner.Position) / this.Owner.velocityMaximum > 11)
            //    inSystem = false;
            //else 
            //    inSystem = this.Owner.GetSystem() == this.SystemToDefend;
            //if (this.SystemToDefend == null)
            //{
            //    this.HasPriorityOrder = false;
            //    this.SystemToDefend = system;
            //    this.OrderQueue.Clear();
            //}
            //else

            ArtificialIntelligence.ShipGoal goal = this.OrderQueue.LastOrDefault();

            this.orderqueue.EnterWriteLock(); //this.Owner.engineState != Ship.MoveState.Warp &&
            if (this.SystemToDefend ==null ||(this.SystemToDefend != system || (this.Owner.GetSystem() != system && goal != null && this.OrderQueue.LastOrDefault().Plan != Plan.DefendSystem)))
			{

#if SHOWSCRUB
                if (this.Target != null && (this.Target as Ship).Name == "Subspace Projector")
                    System.Diagnostics.Debug.WriteLine(string.Concat("Scrubbed", (this.Target as Ship).Name)); 
#endif
                this.SystemToDefend = system;
                this.HasPriorityOrder = false;
				this.SystemToDefend = system;
				this.OrderQueue.Clear();
				if (this.SystemToDefend.PlanetList.Count > 0)
				{
					List<Planet> Potentials = new List<Planet>();
					foreach (Planet p in this.SystemToDefend.PlanetList)
					{
						if (p.Owner == null || p.Owner != this.Owner.loyalty)
						{
							continue;
						}
						Potentials.Add(p);
					}
					if (Potentials.Count > 0)
					{
						int Ran = (int)((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomBetween(0f, (float)Potentials.Count + 0.85f);
						if (Ran > Potentials.Count - 1)
						{
							Ran = Potentials.Count - 1;
						}
                       // this.awaitClosest = Potentials[Ran];
						this.OrderMoveTowardsPosition(Potentials[Ran].Position, 0f, Vector2.One, true,null);
					}
				}
                this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DefendSystem, Vector2.Zero, 0f));
			}
            this.orderqueue.ExitWriteLock();
            this.State = AIState.SystemDefender;
         
            
		}

		public void OrderThrustTowardsPosition(Vector2 position, float desiredFacing, Vector2 fVec, bool ClearOrders)
		{
			if (ClearOrders)
			{
				this.OrderQueue.Clear();
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
			}
			this.FinalFacingVector = fVec;
			this.DesiredFacing = desiredFacing;
			lock (this.wayPointLocker)
			{
				for (int i = 0; i < this.ActiveWayPoints.Count; i++)
				{
					Vector2 waypoint = this.ActiveWayPoints.ToArray()[i];
					if (i == 0)
					{
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateInlineWithVelocity, Vector2.Zero, 0f));
						ArtificialIntelligence.ShipGoal stop = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Stop, Vector2.Zero, 0f);
						this.OrderQueue.AddLast(stop);
						this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.RotateToFaceMovePosition, waypoint, 0f));
						ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MoveToWithin1000, waypoint, desiredFacing)
						{
							SpeedLimit = this.Owner.speed
						};
						this.OrderQueue.AddLast(to1k);
					}
				}
			}
		}

		public void OrderToOrbit(Planet toOrbit, bool ClearOrders)
		{
			if (ClearOrders)
			{
				this.OrderQueue.Clear();
			}
			this.HasPriorityOrder = true;
			lock (this.wayPointLocker)
			{
				this.ActiveWayPoints.Clear();
			}
			this.State = AIState.Orbit;
			this.OrbitTarget = toOrbit;
			ArtificialIntelligence.ShipGoal orbit = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.Orbit, Vector2.Zero, 0f)
			{
				TargetPlanet = toOrbit
			};
			this.OrderQueue.AddLast(orbit);
		}

		
        
        //added by gremlin OrderTrade
        public void OrderTrade()
        {
            //added by gremlin if fleeing keep fleeing
            if (this.Owner.CargoSpace_Max == 0 || this.State == AIState.Flee)
            {
                return;
            }
            try
            {

                //if starting or ending system in combat... clear order...
                if (this.Owner.CargoSpace_Used > 0 && (
                    (this.start != null && this.start.ParentSystem.combatTimer > 0)
                    || (this.end != null && this.end.ParentSystem.combatTimer > 0)))
                {
                    this.start = null;
                    this.end = null;
                    this.OrderQueue.Clear();
                    this.State = AIState.AwaitingOrders;

                }
                //if system all systems in combat... OMG no trade.

                if (this.Owner.loyalty.GetOwnedSystems().Where(combat => combat.combatTimer <= 0).Count() == 0)
                    return;


                //int allincombat = 0;
                //for (int p = 0; p < this.Owner.loyalty.GetPlanets().Count; p++)
                //{
                //    Planet planet = this.Owner.loyalty.GetPlanets()[p];
                //    if (planet.CombatTimer > 0)
                //        continue;
                //    allincombat++;

                //}
                //if (allincombat == 0)
                //    return;
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Clear();
                }
                this.OrderQueue.Clear();
                if (this.Owner.CargoSpace_Used > 0f && this.Owner.GetCargo()["Colonists_1000"] == 0f)
                {
                    #region Deliver Food if already have food

                    if (this.Owner.TradingFood && this.Owner.GetCargo()["Food"] > 0f)
                    {
                        List<Planet> planets = new List<Planet>();
                        for (int i = 0; i < this.Owner.loyalty.GetPlanets().Where(combat => combat.ParentSystem.combatTimer <= 0).Count(); i++)
                        {
                            Planet item = this.Owner.loyalty.GetPlanets()[i];
                            if (item != null)
                            {
                                #region Food AO

                                if (this.Owner.AreaOfOperation.Count > 0)
                                {
                                    foreach (Rectangle areaOfOperation in this.Owner.AreaOfOperation)
                                    {
                                        if (!HelperFunctions.CheckIntersection(areaOfOperation, item.Position) || item.fs != Planet.GoodState.IMPORT || item.FoodHere >= item.MAX_STORAGE * 0.6f)
                                        {
                                            continue;
                                        }
                                        bool flag = false;
                                        float mAXSTORAGE = item.MAX_STORAGE - item.FoodHere;
                                        for (int j = 0; j < this.Owner.loyalty.GetShips().Count; j++)
                                        {
                                            Ship ship = this.Owner.loyalty.GetShips()[j];
                                            if (ship != null && ship.Role == "freighter" && ship != this.Owner)
                                            {
                                                if (ship.GetAI().State == AIState.SystemTrader && ship.GetAI().end == item && ship.GetAI().FoodOrProd == "Food")
                                                {
                                                    mAXSTORAGE = mAXSTORAGE - ship.CargoSpace_Max;
                                                }
                                                if (mAXSTORAGE <= 0f)
                                                {
                                                    flag = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (flag)
                                        {
                                            continue;
                                        }
                                        planets.Add(item);
                                    }
                                }

                                #endregion

                                else if (item.fs == Planet.GoodState.IMPORT && item.FoodHere < item.MAX_STORAGE * 0.6f)
                                {
                                    bool flag1 = false;
                                    float netFoodPerTurn = item.MAX_STORAGE - item.FoodHere;
                                    if (item.NetFoodPerTurn < item.Population / 1000f)
                                    {
                                        netFoodPerTurn = netFoodPerTurn + item.NetFoodPerTurn * 20f;
                                    }
                                    for (int k = 0; k < this.Owner.loyalty.GetShips().Count; k++)
                                    {
                                        Ship item1 = this.Owner.loyalty.GetShips()[k];
                                        if (item1 != null && item1.Role == "freighter" && item1 != this.Owner)
                                        {
                                            if (item1.GetAI().State == AIState.SystemTrader && item1.GetAI().end == item && item1.GetAI().FoodOrProd == "Food")
                                            {
                                                netFoodPerTurn = netFoodPerTurn - item1.CargoSpace_Max;
                                            }
                                            if (netFoodPerTurn <= 0f)
                                            {
                                                flag1 = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (!flag1)
                                    {
                                        planets.Add(item);
                                    }
                                }
                            }
                        }
                        if (planets.Count > 0)
                        {


                            //(dest => (Vector2.Distance(this.Owner.Position, dest.Position)) / (this.Owner.WarpThrust + 1) < 1f ? 0 : (Vector2.Distance(this.Owner.Position, dest.Position)) / (this.Owner.WarpThrust + 1) < 3f ? 1 : (Vector2.Distance(this.Owner.Position, dest.Position)) / (this.Owner.WarpThrust + 1) < 6f ? 2 : 3).ThenBy(dest => dest.MAX_STORAGE - dest.FoodHere);
                            IOrderedEnumerable<Planet> foodHere = planets.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.FoodHere / dest.MAX_STORAGE);
                            //this.Owner.WarpThrust;                                     

                            //from dest in planets
                            //where dest.fs.CompareTo(Planet.GoodState.IMPORT)
                            //orderby dest.FoodHere
                            //dest.ProductionHere / dest.MAX_STORAGE
                            //select dest;
                            //dest => (Vector2.Distance(this.Owner.Position,dest.Position))
                            //if(dest.ProductionHere / dest.MAX_STORAGE < .5 {1} else {0})
                            //dest => dest.FoodHere / dest.MAX_STORAGE < .10f ? 0 : dest.FoodHere / dest.MAX_STORAGE < .3f ?1:2
                            this.end = foodHere.First<Planet>();
                            this.FoodOrProd = "Food";
                            this.OrderMoveTowardsPosition(this.end.Position, 0f, new Vector2(0f, -1f), true,this.end);
                            this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropOffGoods, Vector2.Zero, 0f));
                            this.State = AIState.SystemTrader;
                            return;
                        }
                    }

                    #endregion

                    #region deliver Production

                    else if (this.Owner.TradingProd && this.Owner.GetCargo()["Production"] > 0f)
                    {
                        List<Planet> planets1 = new List<Planet>();
                        this.end = null;
                        for (int l = 0; l < this.Owner.loyalty.GetPlanets().Count; l++)
                        {
                            Planet planet = this.Owner.loyalty.GetPlanets()[l];
                            if (planet != null && planet.ParentSystem.combatTimer < 1)
                            {
                                if (this.Owner.AreaOfOperation.Count > 0)
                                {
                                    foreach (Rectangle rectangle in this.Owner.AreaOfOperation)
                                    {
                                        if (!HelperFunctions.CheckIntersection(rectangle, planet.Position) || planet.ps != Planet.GoodState.IMPORT || planet.ProductionHere >= planet.MAX_STORAGE * 0.75f)
                                        {
                                            continue;
                                        }
                                        bool flag2 = false;
                                        float cargoSpaceMax = planet.MAX_STORAGE - planet.ProductionHere;
                                        for (int m = 0; m < this.Owner.loyalty.GetShips().Count; m++)
                                        {
                                            Ship ship1 = this.Owner.loyalty.GetShips()[m];
                                            if (ship1 != null)
                                            {
                                                if (ship1.Role == "freighter")
                                                {
                                                    if (ship1 == this.Owner)
                                                    {
                                                        goto Label1;
                                                    }
                                                    if (ship1.GetAI().State == AIState.SystemTrader && ship1.GetAI().end == planet && ship1.GetAI().FoodOrProd == "Prod")
                                                    {
                                                        cargoSpaceMax = cargoSpaceMax - ship1.CargoSpace_Max;
                                                    }
                                                }
                                                if (cargoSpaceMax <= 0f)
                                                {
                                                    flag2 = true;
                                                    break;
                                                }
                                            }
                                        Label1:
                                            continue;
                                        }

                                        if (flag2)
                                        {
                                            continue;
                                        }
                                        planets1.Add(planet);
                                    }
                                }
                                else if (planet.ps == Planet.GoodState.IMPORT && planet.ProductionHere < planet.MAX_STORAGE * 0.75f)
                                {
                                    bool flag3 = false;
                                    float single = planet.MAX_STORAGE - planet.ProductionHere;
                                    for (int n = 0; n < this.Owner.loyalty.GetShips().Count; n++)
                                    {
                                        Ship item2 = this.Owner.loyalty.GetShips()[n];
                                        if (item2 != null)
                                        {
                                            if (item2.Role == "freighter")
                                            {
                                                if (item2 == this.Owner)
                                                {
                                                    goto Label0;
                                                }
                                                if (item2.GetAI().State == AIState.SystemTrader && item2.GetAI().end == planet && item2.GetAI().FoodOrProd == "Prod")
                                                {
                                                    single = single - item2.CargoSpace_Max;
                                                }
                                            }
                                            if (single <= 0f)
                                            {
                                                flag3 = true;
                                                break;
                                            }
                                        }
                                    Label0:
                                        continue;
                                    }

                                    if (!flag3)
                                    {
                                        planets1.Add(planet);
                                    }
                                }
                            }
                        }
                        if (planets1.Count > 0)
                        {
                            IOrderedEnumerable<Planet> productionHere = planets1.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.ProductionHere / dest.MAX_STORAGE);
                            //IOrderedEnumerable<Planet> productionHere =
                            //from dest in planets1

                            //orderby dest.ProductionHere / dest.MAX_STORAGE
                            //select dest;
                            this.end = productionHere.First<Planet>();
                            this.FoodOrProd = "Prod";
                            this.OrderMoveTowardsPosition(this.end.Position, 0f, new Vector2(0f, -1f), true,this.end);
                            this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropOffGoods, Vector2.Zero, 0f));
                            this.State = AIState.SystemTrader;
                            return;
                        }
                    }

                    #endregion
                }
                #region deliver Food2
                this.start = null;
                this.end = null;
                this.FoodOrProd = "";
                List<Planet> planets2 = new List<Planet>();
                if (this.Owner.loyalty.data.Traits.Cybernetic == 1)
                {
                    this.Owner.TradingFood = false;
                }
                if (this.Owner.TradingFood)
                {
                    for (int o = 0; o < this.Owner.loyalty.GetPlanets().Count(); o++)
                    {
                        Planet planet1 = this.Owner.loyalty.GetPlanets()[o];
                        if (planet1 != null && planet1.ParentSystem.combatTimer < 1)
                        {
                            if (this.Owner.AreaOfOperation.Count > 0)
                            {
                                foreach (Rectangle areaOfOperation1 in this.Owner.AreaOfOperation)
                                {
                                    if (!HelperFunctions.CheckIntersection(areaOfOperation1, planet1.Position) || planet1.fs != Planet.GoodState.IMPORT || planet1.FoodHere >= planet1.MAX_STORAGE * 0.6f)
                                    {
                                        continue;
                                    }
                                    bool flag4 = false;
                                    float mAXSTORAGE1 = planet1.MAX_STORAGE - planet1.FoodHere;
                                    for (int p = 0; p < this.Owner.loyalty.GetShips().Count; p++)
                                    {
                                        Ship ship2 = this.Owner.loyalty.GetShips()[p];
                                        if (ship2 != null && ship2.Role == "freighter" && ship2 != this.Owner)
                                        {
                                            if (ship2.GetAI().State == AIState.SystemTrader && ship2.GetAI().end == planet1 && ship2.CargoSpace_Max + planet1.FoodHere > 0.75f * planet1.MAX_STORAGE && ship2.GetAI().FoodOrProd == "Food")
                                            {
                                                mAXSTORAGE1 = mAXSTORAGE1 - ship2.CargoSpace_Max;
                                            }
                                            if (mAXSTORAGE1 <= 0f)
                                            {
                                                flag4 = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (flag4)
                                    {
                                        continue;
                                    }
                                    planets2.Add(planet1);
                                }
                            }
                            else if (planet1.fs == Planet.GoodState.IMPORT && planet1.FoodHere < planet1.MAX_STORAGE * 0.6f)
                            {
                                bool flag5 = false;
                                float netFoodPerTurn1 = planet1.MAX_STORAGE - planet1.FoodHere;
                                if (planet1.NetFoodPerTurn < planet1.Population / 1000f)
                                {
                                    netFoodPerTurn1 = netFoodPerTurn1 + planet1.NetFoodPerTurn * 20f;
                                }
                                for (int q = 0; q < this.Owner.loyalty.GetShips().Count; q++)
                                {
                                    Ship item3 = this.Owner.loyalty.GetShips()[q];
                                    if (item3 != null && item3.Role == "freighter" && item3 != this.Owner)
                                    {
                                        if (item3.GetAI().State == AIState.SystemTrader && item3.GetAI().end == planet1 && item3.GetAI().FoodOrProd == "Food")
                                        {
                                            netFoodPerTurn1 = netFoodPerTurn1 - item3.CargoSpace_Max;
                                        }
                                        if (netFoodPerTurn1 <= 0f)
                                        {
                                            flag5 = true;
                                            break;
                                        }
                                    }
                                }
                                if (!flag5)
                                {
                                    planets2.Add(planet1);
                                }
                            }
                        }
                    }
                    if (planets2.Count > 0)
                    {
                        //IOrderedEnumerable<Planet> foodHere = planets2.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.GetFTLSpeed() + 1))).ThenBy(dest => dest.FoodHere / dest.MAX_STORAGE);
                        IOrderedEnumerable<Planet> foodHere = planets2.OrderBy(dest => (Vector2.Distance(this.Owner.Position, dest.Position)) / (this.Owner.WarpThrust + 1) < 5f ? 0 : (Vector2.Distance(this.Owner.Position, dest.Position)) / (this.Owner.WarpThrust + 1) < 10f ? 1 : 2).ThenBy(dest => dest.FoodHere / dest.MAX_STORAGE);
                        //IOrderedEnumerable<Planet> foodHere1 =
                        //                                      from dest in planets2
                        //                                      orderby dest.FoodHere / dest.MAX_STORAGE
                        //                                      select dest;
                        this.end = foodHere.First<Planet>();
                        this.FoodOrProd = "Food";
                    }
                #endregion
                    #region Get Food
                    if (this.end != null)
                    {
                        planets2 = new List<Planet>();
                        for (int r = 0; r < this.Owner.loyalty.GetPlanets().Count(); r++)
                        {
                            Planet planet2 = this.Owner.loyalty.GetPlanets()[r];
                            if (planet2 != null && planet2 != this.end && planet2.ParentSystem.combatTimer < 1)
                            {
                                #region AO
                                if (this.Owner.AreaOfOperation.Count > 0)
                                {
                                    foreach (Rectangle rectangle1 in this.Owner.AreaOfOperation)
                                    {
                                        if (!HelperFunctions.CheckIntersection(rectangle1, planet2.Position) || !(this.FoodOrProd == "Food"))
                                        {
                                            continue;
                                        }
                                        if (planet2.fs == Planet.GoodState.EXPORT && planet2.FoodHere > 10f)
                                        {
                                            planets2.Add(planet2);
                                        }
                                        if (planets2.Count <= 0)
                                        {
                                            continue;
                                        }
                                        IOrderedEnumerable<Planet> mAXSTORAGE2 =
                                                                                from dest in planets2
                                                                                orderby dest.MAX_STORAGE - dest.FoodHere
                                                                                select dest;
                                        this.start = mAXSTORAGE2.First<Planet>();
                                    }
                                }
                                #endregion
                                else if (this.FoodOrProd == "Food")
                                {
                                    if (planet2.fs == Planet.GoodState.EXPORT && planet2.FoodHere > 10f)
                                    {
                                        planets2.Add(planet2);
                                    }
                                    if (planets2.Count > 0)
                                    {

                                        IOrderedEnumerable<Planet> mAXSTORAGE = planets2.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.MAX_STORAGE - dest.FoodHere);

                                        //IOrderedEnumerable<Planet> mAXSTORAGE =
                                        //                                        from dest in planets2
                                        //                                        orderby dest.MAX_STORAGE - dest.FoodHere
                                        //                                        select dest;
                                        if (mAXSTORAGE.First<Planet>() != null)
                                        {
                                            this.start = mAXSTORAGE.First<Planet>();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    #endregion
                }
                #region Get Production

                if (this.Owner.TradingProd && this.start == null)
                {
                    this.end = null;
                    for (int s = 0; s < this.Owner.loyalty.GetPlanets().Count(); s++)
                    {
                        Planet planet3 = this.Owner.loyalty.GetPlanets()[s];
                        if (planet3 != null && planet3.ParentSystem.combatTimer < 1)
                        {
                            if (this.Owner.AreaOfOperation.Count > 0)
                            {
                                foreach (Rectangle areaOfOperation2 in this.Owner.AreaOfOperation)
                                {
                                    if (!HelperFunctions.CheckIntersection(areaOfOperation2, planet3.Position) || planet3.ps != Planet.GoodState.IMPORT || planet3.ProductionHere >= planet3.MAX_STORAGE * 0.75f)
                                    {
                                        continue;
                                    }
                                    bool flag6 = false;
                                    float cargoSpaceMax1 = planet3.MAX_STORAGE - planet3.ProductionHere;
                                    for (int t = 0; t < this.Owner.loyalty.GetShips().Count; t++)
                                    {
                                        Ship ship3 = this.Owner.loyalty.GetShips()[t];
                                        if (ship3 != null)
                                        {
                                            if (ship3.Role == "freighter")
                                            {
                                                if (ship3 == this.Owner)
                                                {
                                                    goto Label3;
                                                }
                                                if (ship3.GetAI().State == AIState.SystemTrader && ship3.GetAI().end == planet3 && ship3.GetAI().FoodOrProd == "Prod")
                                                {
                                                    cargoSpaceMax1 = cargoSpaceMax1 - ship3.CargoSpace_Max;
                                                }
                                            }
                                            if (cargoSpaceMax1 <= 0f)
                                            {
                                                flag6 = true;
                                                break;
                                            }
                                        }
                                    Label3:
                                        continue;
                                    }
                                    if (flag6)
                                    {
                                        continue;
                                    }
                                    planets2.Add(planet3);
                                }
                            }
                            else if (planet3.ps == Planet.GoodState.IMPORT && planet3.ProductionHere < planet3.MAX_STORAGE * 0.75f)
                            {
                                bool flag7 = false;
                                float single1 = planet3.MAX_STORAGE - planet3.ProductionHere;
                                for (int u = 0; u < this.Owner.loyalty.GetShips().Count; u++)
                                {
                                    Ship item4 = this.Owner.loyalty.GetShips()[u];
                                    if (item4 != null)
                                    {
                                        if (item4.Role == "freighter")
                                        {
                                            if (item4 == this.Owner)
                                            {
                                                goto Label2;
                                            }
                                            if (item4.GetAI().State == AIState.SystemTrader && item4.GetAI().end == planet3 && item4.GetAI().FoodOrProd == "Prod")
                                            {
                                                single1 = single1 - item4.CargoSpace_Max;
                                            }
                                        }
                                        if (single1 <= 0f)
                                        {
                                            flag7 = true;
                                            break;
                                        }
                                    }
                                Label2:
                                    continue;
                                }
                                if (!flag7)
                                {
                                    planets2.Add(planet3);
                                }
                            }
                        }
                    }
                    if (planets2.Count > 0)
                    {

                        IOrderedEnumerable<Planet> productionHere1 = planets2.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.ProductionHere / dest.MAX_STORAGE);
                        //                                            from dest in planets2
                        //                                            orderby dest.ProductionHere / dest.MAX_STORAGE
                        //                                            select dest;
                        if (productionHere1.Count() > 0)
                        {
                            this.end = productionHere1.First<Planet>();
                            this.FoodOrProd = "Prod";
                        }
                    }
                    if (this.end != null)
                    {
                        planets2 = new List<Planet>();
                        for (int v = 0; v < this.Owner.loyalty.GetPlanets().Count(); v++)
                        {
                            Planet planet4 = this.Owner.loyalty.GetPlanets()[v];
                            if (planet4 != null && planet4 != this.end && planet4.ParentSystem.combatTimer < 1)
                            {
                                if (this.Owner.AreaOfOperation.Count > 0)
                                {
                                    foreach (Rectangle rectangle2 in this.Owner.AreaOfOperation)
                                    {
                                        if (!HelperFunctions.CheckIntersection(rectangle2, planet4.Position) || !(this.FoodOrProd == "Prod"))
                                        {
                                            continue;
                                        }
                                        if (planet4.ps == Planet.GoodState.EXPORT && planet4.ProductionHere > 10f)
                                        {
                                            planets2.Add(planet4);
                                        }
                                        if (planets2.Count <= 0)
                                        {
                                            continue;
                                        }
                                        IOrderedEnumerable<Planet> planets3 = planets2.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.MAX_STORAGE - dest.ProductionHere);
                                        //IOrderedEnumerable<Planet> planets3 =
                                        //                                     from dest in planets2
                                        //                                     orderby dest.MAX_STORAGE - dest.ProductionHere
                                        //                                     select dest;
                                        this.start = planets3.First<Planet>();
                                    }
                                }
                                else if (this.FoodOrProd == "Prod")
                                {
                                    if (planet4.ps == Planet.GoodState.EXPORT && planet4.ProductionHere > 10f)
                                    {
                                        planets2.Add(planet4);
                                    }
                                    if (planets2.Count > 0)
                                    {
                                        IOrderedEnumerable<Planet> mAXSTORAGE4 = planets2.OrderBy(dest => Math.Ceiling(Vector2.Distance(this.Owner.Position, dest.Position) / (this.Owner.velocityMaximum + 1))).ThenBy(dest => dest.MAX_STORAGE - dest.ProductionHere);
                                        //IOrderedEnumerable<Planet> mAXSTORAGE4 =
                                        //                                        from dest in planets2
                                        //                                        orderby dest.MAX_STORAGE - dest.ProductionHere
                                        //                                        select dest;
                                        this.start = mAXSTORAGE4.First<Planet>();
                                    }
                                }
                            }
                        }
                    }
                }

                #endregion
                if (this.start != null && this.end != null && !String.IsNullOrEmpty(this.FoodOrProd))
                {
                    this.OrderMoveTowardsPosition(this.start.Position + (RandomMath.RandomDirection() * 500f), 0f, new Vector2(0f, -1f), true,this.start);
                    this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.PickupGoods, Vector2.Zero, 0f));
                }
                this.State = AIState.SystemTrader;
            }
            catch { }
        }


		public void OrderTradeFromSave(bool hasCargo, Guid startGUID, Guid endGUID)
		{
            if (this.Owner.CargoSpace_Max == 0 || this.State == AIState.Flee)
            {
                return;
            }
            if (this.Owner.loyalty.GetOwnedSystems().Where(combat => combat.combatTimer < 1).Count() == 0)
                return;
            if (this.Owner.CargoSpace_Max >0 
                && (this.start != null && this.start.ParentSystem.combatTimer >0)
                || (this.end != null && this.end.ParentSystem.CombatInSystem))
            {
                this.start = null;
                this.end = null;
                this.OrderQueue.Clear();
                this.State = AIState.AwaitingOrders;

            }
            
            if (this.start == null && this.end == null)
			{
				foreach (Planet p in this.Owner.loyalty.GetPlanets())
				{
					if (p.guid == startGUID)
					{
						this.start = p;
					}
					if (p.guid != endGUID)
					{
						continue;
					}
					this.end = p;
				}
			}
			if (!hasCargo && this.start != null)
			{
				this.OrderMoveTowardsPosition(this.start.Position + (RandomMath.RandomDirection() * 500f), 0f, new Vector2(0f, -1f), true,this.start);
				this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.PickupGoods, Vector2.Zero, 0f));
				this.State = AIState.SystemTrader;
			}
			if (!hasCargo || this.end == null)
			{
				if (!hasCargo && (this.start == null || this.end == null))
				{
					this.OrderTrade();
				}
				return;
			}
			this.OrderMoveTowardsPosition(this.end.Position + (RandomMath.RandomDirection() * 500f), 0f, new Vector2(0f, -1f), true,this.end);
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropOffGoods, Vector2.Zero, 0f));
			this.State = AIState.SystemTrader;
		}


        public void OrderTransportPassengers()
        {
            float closestD;
            float Distance;

            List<SolarSystem> OwnedSystems = new List<SolarSystem>(this.Owner.loyalty.GetOwnedSystems());
            if (OwnedSystems.Where(combat => combat.combatTimer < 1).Count() == 0)
                return;
            if (this.Owner.CargoSpace_Max > 0
                && (this.start != null && this.start.ParentSystem.combatTimer > 0)
                || (this.end != null && this.end.ParentSystem.combatTimer > 0))
            {
                this.start = null;
                this.end = null;
                this.OrderQueue.Clear();
                this.State = AIState.AwaitingOrders;

            }
            if (!this.Owner.GetCargo().ContainsKey("Colonists_1000"))
            {
                this.Owner.GetCargo().Add("Colonists_1000", 0f);
            }
            List<Planet> SafePlanets = new List<Planet>(this.Owner.loyalty.GetPlanets());
            SafePlanets = SafePlanets.Where(combat => combat.ParentSystem.combatTimer <= 0).ToList();

            List<Planet> Possible = new List<Planet>();

            // fbedard: Where to drop nearest Population
            if (this.Owner.GetCargo()["Colonists_1000"] > 0f)
            {
                foreach (Planet p in SafePlanets)
                {
                    if (p == this.start)
                    {
                        continue;
                    }
                    if (this.Owner.AreaOfOperation.Count <= 0)
                    {
                        if (((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                    else
                    {
                        foreach (Rectangle AO in this.Owner.AreaOfOperation)
                        {
                            if (!HelperFunctions.CheckIntersection(AO, p.Position) || ((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                            {
                                continue;
                            }
                            Possible.Add(p);
                        }
                    }
                }

                closestD = 999999999f;
                this.end = null;
                this.OrderQueue.Clear();
                foreach (Planet p in Possible)
                {
                    Distance = Vector2.Distance(this.Owner.Center, p.Position);
                    if (Distance >= closestD)
                    {
                        continue;
                    }
                    closestD = Distance;
                    this.end = p;
                }
                if (this.end != null)
                {
                    this.OrderMoveTowardsPosition(this.end.Position, 0f, new Vector2(0f, -1f), true, this.end);
                    this.State = AIState.PassengerTransport;
                    this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropoffPassengers, Vector2.Zero, 0f));
                }
                return;
            }

            //fbedard: Where to load nearest Population
            this.start = null;
            this.OrderQueue.Clear();
            Possible = new List<Planet>();
            foreach (Planet p in SafePlanets)
            {
                if (this.Owner.AreaOfOperation.Count <= 0)
                {
                    //if (p.Population <= 1000f)
                    if (p.Population <= 2000f)
                    {
                        continue;
                    }
                    Possible.Add(p);
                }
                else
                {
                    foreach (Rectangle AO in this.Owner.AreaOfOperation)
                    {
                        //if (!HelperFunctions.CheckIntersection(AO, p.Position) || p.Population <= 1500f)
                        if (!HelperFunctions.CheckIntersection(AO, p.Position) || p.Population <= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                }
            }
            closestD = 999999999f;
            foreach (Planet p in Possible)
            {
                Distance = Vector2.Distance(this.Owner.Center, p.Position);
                if (Distance >= closestD)
                {
                    continue;
                }
                closestD = Distance;
                this.start = p;
            }

            // fbedard: Where to drop nearest Population
            this.end = null;
            Possible = new List<Planet>();
            foreach (Planet p in SafePlanets)
            {
                if (p == this.start)
                {
                    continue;
                }
                if (this.Owner.AreaOfOperation.Count <= 0)
                {
                    //if ((double)(p.MaxPopulation - p.Population) <= 0.5 * (double)p.MaxPopulation || p.Population >= 1000f)
                    if (((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                    {
                        continue;
                    }
                    Possible.Add(p);
                }
                else
                {
                    foreach (Rectangle AO in this.Owner.AreaOfOperation)
                    {
                        //if (!HelperFunctions.CheckIntersection(AO, p.Position) || (double)(p.MaxPopulation - p.Population) <= 0.5 * (double)p.MaxPopulation || p.Population >= 1000f || p == this.start)
                        if (!HelperFunctions.CheckIntersection(AO, p.Position) || ((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                }
            }

            closestD = 999999999f;
            foreach (Planet p in Possible)
            {
                Distance = Vector2.Distance(this.Owner.Center, p.Position);
                if (Distance >= closestD)
                {
                    continue;
                }
                closestD = Distance;
                this.end = p;
            }

            if (this.start != null && this.end != null)
            {
                this.OrderMoveTowardsPosition(this.start.Position, 0f, new Vector2(0f, -1f), true, this.start);
                this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.PickupPassengers, Vector2.Zero, 0f));
            }
            this.State = AIState.PassengerTransport;
        }

		public void OrderTransportPassengersFromSave()
		{
            float closestD;
            float Distance;

            if (this.Owner.loyalty.GetOwnedSystems().Where(combat => combat.combatTimer < 1).Count() == 0)
                return;
            if (this.Owner.CargoSpace_Max > 0
                && (this.start != null && this.start.ParentSystem.combatTimer > 0)
                || (this.end != null && this.end.ParentSystem.combatTimer > 0))
            {
                this.start = null;
                this.end = null;
                this.OrderQueue.Clear();
                this.State = AIState.AwaitingOrders;

            }
 
            if (!this.Owner.GetCargo().ContainsKey("Colonists_1000"))
			{
				this.Owner.GetCargo().Add("Colonists_1000", 0f);
			}

            List<Planet> Possible = new List<Planet>();

            // fbedard: Where to drop nearest Population
            if (this.Owner.GetCargo()["Colonists_1000"] > 0f)
            {
                foreach (Planet p in this.Owner.loyalty.GetPlanets())
                {
                    if (p == this.start)
                    {
                        continue;
                    }
                    if (this.Owner.AreaOfOperation.Count <= 0)
                    {
                        //if (p.Population <= 1500f)  That was wrong !!!
                        if (((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                    else
                    {
                        foreach (Rectangle AO in this.Owner.AreaOfOperation)
                        {
                            if (!HelperFunctions.CheckIntersection(AO, p.Position) || ((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                            {
                                continue;
                            }
                            Possible.Add(p);
                        }
                    }
                }

                closestD = 999999999f;
                this.end = null;
                this.OrderQueue.Clear();
                foreach (Planet p in Possible)
                {
                    Distance = Vector2.Distance(this.Owner.Center, p.Position);
                    if (Distance >= closestD)
                    {
                        continue;
                    }
                    closestD = Distance;
                    this.end = p;
                }
                if (this.end != null)
                {
                    this.OrderMoveTowardsPosition(this.end.Position, 0f, new Vector2(0f, -1f), true, this.end);
                    this.State = AIState.PassengerTransport;
                    this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropoffPassengers, Vector2.Zero, 0f));
                }
                return;
            }

            //fbedard: Where to load nearest Population
            this.start = null;
            this.OrderQueue.Clear();
            Possible = new List<Planet>();
            foreach (Planet p in this.Owner.loyalty.GetPlanets())
            {
                if (this.Owner.AreaOfOperation.Count <= 0)
                {
                    //if (p.Population <= 1000f)
                    if (p.Population <= 2000f)
                    {
                        continue;
                    }
                    Possible.Add(p);
                }
                else
                {
                    foreach (Rectangle AO in this.Owner.AreaOfOperation)
                    {
                        //if (!HelperFunctions.CheckIntersection(AO, p.Position) || p.Population <= 1500f)
                        if (!HelperFunctions.CheckIntersection(AO, p.Position) || p.Population <= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                }
            }
            closestD = 999999999f;
            foreach (Planet p in Possible)
            {
                Distance = Vector2.Distance(this.Owner.Center, p.Position);
                if (Distance >= closestD)
                {
                    continue;
                }
                closestD = Distance;
                this.start = p;
            }

            // fbedard: Where to drop nearest Population
            this.end = null;
            Possible = new List<Planet>();
            foreach (Planet p in this.Owner.loyalty.GetPlanets())
            {
                if (p == this.start)
                {
                    continue;
                }
                if (this.Owner.AreaOfOperation.Count <= 0)
                {
                    //if ((double)(p.MaxPopulation - p.Population) <= 0.5 * (double)p.MaxPopulation || p.Population >= 1000f)
                    if (((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)

                    {
                        continue;
                    }
                    Possible.Add(p);
                }
                else
                {
                    foreach (Rectangle AO in this.Owner.AreaOfOperation)
                    {
                        //if (!HelperFunctions.CheckIntersection(AO, p.Position) || (double)(p.MaxPopulation - p.Population) <= 0.5 * (double)p.MaxPopulation || p.Population >= 1000f || p == this.start)
                        if (!HelperFunctions.CheckIntersection(AO, p.Position) || ((p.Population / p.MaxPopulation) >= 0.8 && p.MaxPopulation <= 2000f) || p.Population >= 2000f)
                        {
                            continue;
                        }
                        Possible.Add(p);
                    }
                }
            }

            closestD = 999999999f;
            foreach (Planet p in Possible)
            {
                Distance = Vector2.Distance(this.Owner.Center, p.Position);
                if (Distance >= closestD)
                {
                    continue;
                }
                closestD = Distance;
                this.end = p;
            }

            if (this.start != null && this.end != null)
            {
                this.OrderMoveTowardsPosition(this.start.Position, 0f, new Vector2(0f, -1f), true, this.start);
                this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.PickupPassengers, Vector2.Zero, 0f));
            }
            this.State = AIState.PassengerTransport;
        }

		public void OrderTroopToBoardShip(Ship s)
		{
			this.HasPriorityOrder = true;
			this.EscortTarget = s;
			ArtificialIntelligence.ShipGoal g = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.BoardShip, Vector2.Zero, 0f);
            this.orderqueue.EnterWriteLock();
            this.OrderQueue.Clear();
			this.OrderQueue.AddLast(g);
            this.orderqueue.ExitWriteLock();
		}

		public void OrderTroopToShip(Ship s)
		{
			this.EscortTarget = s;
			ArtificialIntelligence.ShipGoal g = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.TroopToShip, Vector2.Zero, 0f);
			this.OrderQueue.Clear();
			this.OrderQueue.AddLast(g);
		}

		private void PickupGoods()
		{
            if (this.start == null)
                return;
            if (this.FoodOrProd == "Food")
			{
				if (this.Owner.GetCargo()["Production"] > 0f)
				{
					Planet productionHere = this.start;
					productionHere.ProductionHere = productionHere.ProductionHere + this.Owner.GetCargo()["Production"];
					this.Owner.GetCargo()["Production"] = 0f;
				}
				if (this.Owner.GetCargo()["Colonists_1000"] > 0f)
				{
					Planet population = this.start;
					population.Population = population.Population + this.Owner.GetCargo()["Colonists_1000"] * (float)this.Owner.loyalty.data.Traits.PassengerModifier;
					this.Owner.GetCargo()["Colonists_1000"] = 0f;
				}
				if (this.start.FoodHere < 10f)
				{
					this.OrderTrade();
				}
				else
				{
					while (this.start.FoodHere > 0f && (int)this.Owner.CargoSpace_Max - (int)this.Owner.CargoSpace_Used > 0)
					{
						this.Owner.AddGood("Food", 1);
						Planet foodHere = this.start;
						foodHere.FoodHere = foodHere.FoodHere - 1f;
					}
					this.OrderMoveTowardsPosition(this.end.Position + (((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomDirection() * 500f), 0f, new Vector2(0f, -1f), true,this.end);
					this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropOffGoods, Vector2.Zero, 0f));
					this.State = AIState.SystemTrader;
				}
			}
			else if (this.FoodOrProd != "Prod")
			{
				this.OrderTrade();
			}
			else
			{
				if (this.Owner.GetCargo()["Food"] > 0f)
				{
					Planet planet = this.start;
					planet.FoodHere = planet.FoodHere + this.Owner.GetCargo()["Food"];
					this.Owner.GetCargo()["Food"] = 0f;
				}
				if (this.Owner.GetCargo()["Colonists_1000"] > 0f)
				{
					Planet population1 = this.start;
					population1.Population = population1.Population + this.Owner.GetCargo()["Colonists_1000"] * (float)this.Owner.loyalty.data.Traits.PassengerModifier;
					this.Owner.GetCargo()["Colonists_1000"] = 0f;
				}
				if (this.start.ProductionHere < 10f)
				{
					this.OrderTrade();
				}
				else
				{
					while (this.start.ProductionHere > 0f && (int)this.Owner.CargoSpace_Max - (int)this.Owner.CargoSpace_Used > 0)
					{
						this.Owner.AddGood("Production", 1);
						Planet productionHere1 = this.start;
						productionHere1.ProductionHere = productionHere1.ProductionHere - 1f;
					}
					this.OrderMoveTowardsPosition(this.end.Position + (((this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG)).RandomDirection() * 500f), 0f, new Vector2(0f, -1f), true,this.end);
					this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropOffGoods, Vector2.Zero, 0f));
					this.State = AIState.SystemTrader;
				}
			}
			this.State = AIState.SystemTrader;
		}

		private void PickupPassengers()
		{
			if (this.Owner.GetCargo()["Production"] > 0f)
			{
				Planet productionHere = this.start;
				productionHere.ProductionHere = productionHere.ProductionHere + this.Owner.GetCargo()["Production"];
				this.Owner.GetCargo()["Production"] = 0f;
			}
			if (this.Owner.GetCargo()["Food"] > 0f)
			{
				Planet foodHere = this.start;
				foodHere.FoodHere = foodHere.FoodHere + this.Owner.GetCargo()["Food"];
				this.Owner.GetCargo()["Food"] = 0f;
			}
			while (this.Owner.CargoSpace_Used < this.Owner.CargoSpace_Max)
			{
				this.Owner.AddGood("Colonists_1000", 1);
				Planet population = this.start;
				population.Population = population.Population - (float)this.Owner.loyalty.data.Traits.PassengerModifier;
			}
			this.OrderQueue.RemoveFirst();
			this.OrderMoveTowardsPosition(this.end.Position, 0f, new Vector2(0f, -1f), true,this.end);
			this.State = AIState.PassengerTransport;
			this.OrderQueue.AddLast(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DropoffPassengers, Vector2.Zero, 0f));
		}

		private void PlotCourseToNew(Vector2 endPos, Vector2 startPos)
		{
			float Distance = Vector2.Distance(startPos, endPos);
			if (Distance <= this.Owner.CalculateRange())
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Enqueue(endPos);
				}
				return;
			}
			List<Vector2> PotentialWayPoints = new List<Vector2>();
			foreach (Ship ship in this.Owner.loyalty.GetShips())
			{
				if (!(ship.Name == "Subspace Projector") || Vector2.Distance(ship.Center, endPos) >= Distance)
				{
					continue;
				}
				PotentialWayPoints.Add(ship.Center);
			}
			foreach (Planet p in this.Owner.loyalty.GetPlanets())
			{
				if (Vector2.Distance(p.Position, endPos) >= Distance)
				{
					continue;
				}
				PotentialWayPoints.Add(p.Position);
			}
			IOrderedEnumerable<Vector2> sortedList = 
				from point in PotentialWayPoints
				orderby Vector2.Distance(startPos, point)
				select point;
			List<Vector2> Closest3 = new List<Vector2>();
			int i = 0;
			using (IEnumerator<Vector2> enumerator = sortedList.GetEnumerator())
			{
				do
				{
					if (!enumerator.MoveNext())
					{
						break;
					}
					Closest3.Add(enumerator.Current);
					i++;
				}
				while (i != 3);
			}
			sortedList = 
				from point in Closest3
				orderby Vector2.Distance(point, endPos)
				select point;
			bool gotWayPoint = false;
			if (sortedList.Count<Vector2>() > 0)
			{
				if (Vector2.Distance(endPos, startPos) >= Vector2.Distance(startPos, sortedList.First<Vector2>()))
				{
					lock (this.wayPointLocker)
					{
						this.ActiveWayPoints.Enqueue(sortedList.First<Vector2>());
					}
					this.PlotCourseToNew(endPos, sortedList.First<Vector2>());
					gotWayPoint = true;
				}
				else
				{
					gotWayPoint = false;
				}
			}
			if (!gotWayPoint)
			{
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Enqueue(endPos);
				}
			}
		}

		private void RotateInLineWithVelocity(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
		{
			if (this.Owner.Velocity == Vector2.Zero)
			{
				this.OrderQueue.RemoveFirst();
				return;
			}
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(Vector2.Normalize(this.Owner.Velocity), forward));
			float facing = (Vector2.Dot(Vector2.Normalize(this.Owner.Velocity), right) > 0f ? 1f : -1f);
			if (angleDiff <= 0.2f)
			{
				this.OrderQueue.RemoveFirst();
				return;
			}
			this.RotateToFacing(elapsedTime, angleDiff, facing);
		}

		private void RotateToDesiredFacing(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
			Vector2 p = HelperFunctions.findPointFromAngleAndDistanceUsingRadians(Vector2.Zero, goal.DesiredFacing, 1f);
			Vector2 fvec = HelperFunctions.FindVectorToTarget(Vector2.Zero, p);
			Vector2 wantedForward = Vector2.Normalize(fvec);
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
			float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
			if (angleDiff <= 0.02f)
			{
				this.OrderQueue.RemoveFirst();
				return;
			}
			this.RotateToFacing(elapsedTime, angleDiff, facing);
		}

		private bool RotateToFaceMovePosition(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
            bool turned = false;
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			Vector2 right = new Vector2(-forward.Y, forward.X);
			Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, goal.MovePosition);
			float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, forward));
			if (angleDiff > 0.2f)
			{
				this.Owner.HyperspaceReturn();
				this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, right) > 0f ? 1f : -1f));
                turned = true;
			}
			else if (this.OrderQueue.Count > 0)
			{
				this.OrderQueue.RemoveFirst();
				
			}
            return turned;
		}
        private bool RotateToFaceMovePosition(float elapsedTime, Vector2 MovePosition)
        {
            bool turned = false;
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
            Vector2 right = new Vector2(-forward.Y, forward.X);
            Vector2 VectorToTarget = HelperFunctions.FindVectorToTarget(this.Owner.Center, MovePosition);
            float angleDiff = (float)Math.Acos((double)Vector2.Dot(VectorToTarget, forward));
            if (angleDiff > this.Owner.rotationRadiansPerSecond*elapsedTime )
            {
                this.Owner.HyperspaceReturn();
                this.RotateToFacing(elapsedTime, angleDiff, (Vector2.Dot(VectorToTarget, right) > 0f ? 1f : -1f));
                turned = true;
            }
 
            return turned;
        }
		private void RotateToFacing(float elapsedTime, float angleDiff, float facing)
		{
			this.Owner.isTurning = true;
			float RotAmount = Math.Min(angleDiff, facing * elapsedTime * this.Owner.rotationRadiansPerSecond);
			if (Math.Abs(RotAmount) > angleDiff)
			{
				RotAmount = (RotAmount <= 0f ? -angleDiff : angleDiff);
			}
			if (RotAmount > 0f)
			{
				if (this.Owner.yRotation > -this.Owner.maxBank)
				{
					Ship owner = this.Owner;
					owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
				}
			}
			else if (RotAmount < 0f && this.Owner.yRotation < this.Owner.maxBank)
			{
				Ship ship = this.Owner;
				ship.yRotation = ship.yRotation + this.Owner.yBankAmount;
			}
			if (!float.IsNaN(RotAmount))
			{
				Ship rotation = this.Owner;
				rotation.Rotation = rotation.Rotation + RotAmount;
			}
		}

        //added by gremlin Deveksmod Scan for combat targets.
        public GameplayObject ScanForCombatTargets(Vector2 Position, float Radius)
        {

            RandomThreadMath randomThreadMath;
            //reset target lists; ScanForTargets "refreshes" lists
            this.BadGuysNear = false;
            this.FriendliesNearby.Clear();
            this.PotentialTargets.Clear();
            this.NearbyShips.Clear();
            //this.TrackProjectiles.Clear();

            //if hasPriorityTarget, and target is null
            if (this.hasPriorityTarget && this.Target == null)
            {
                //remove boolean  hasPriorityTarget state: target is null
                this.hasPriorityTarget = false;
                //this ship has queued target orders
                if (this.TargetQueue.Count > 0)
                {
                    this.hasPriorityTarget = true;
                    this.Target = this.TargetQueue.First<Ship>();// dequeue
                }
            }
            //ship has a target
            if (this.Target != null)
            {
                //ship cannot friendly-fire
                if ((this.Target as Ship).loyalty == this.Owner.loyalty)
                {
                    this.Target = null;
                    this.hasPriorityTarget = false;
                }

                //target ship is hostile & at warp & not currently intercepted
                //disengage pursuit
                else if (!this.Intercepting && (this.Target as Ship).engineState == Ship.MoveState.Warp) //||((double)Vector2.Distance(Position, this.Target.Center) > (double)Radius ||
                {
                    //remove target from consideration
                    this.Target = (GameplayObject)null;
                    //ship does not have priority order 
                    //& ship does not belong to player (I assume this exists to allow the Human Player to target units at warp; thus make exception case)
                    //return ship to AIState.AwaitingOrders
                    if (!this.HasPriorityOrder && this.Owner.loyalty != ArtificialIntelligence.universeScreen.player)
                        this.State = AIState.AwaitingOrders;
                    return (GameplayObject)null; 
                }
            }
            this.CombatAI.PreferredEngagementDistance = this.Owner.maxWeaponsRange * 0.66f;
            SolarSystem thisSystem = this.Owner.GetSystem(); //determine system this ship currently occupies
            {
                #region Ship Escort Logic
                if (this.EscortTarget != null && this.EscortTarget.Active && this.EscortTarget.GetAI().Target != null)
                {
                    ArtificialIntelligence.ShipWeight sw = new ArtificialIntelligence.ShipWeight();
                    sw.ship = this.EscortTarget.GetAI().Target as Ship;
                    sw.weight = 2f;
                    this.NearbyShips.Add(sw);
                }
                List<GameplayObject> nearby = UniverseScreen.ShipSpatialManager.GetNearby(Owner);
                for (int i = 0; i < nearby.Count; i++)
                {
                    Ship item1 = nearby[i] as Ship;
                    if (item1 != null && item1.Active && !item1.dying &&(Vector2.Distance(this.Owner.Center, item1.Center) <= Radius))
                    {
                        Empire empire = item1.loyalty;
                        Ship shipTarget = item1.GetAI().Target as Ship;
                        if (empire == Owner.loyalty)
                        {
                            this.FriendliesNearby.Add(item1);
                        }
                        else if (empire != this.Owner.loyalty
                            && shipTarget != null
                            && shipTarget == this.EscortTarget && item1.engineState != Ship.MoveState.Warp)
                        {

                            ArtificialIntelligence.ShipWeight sw = new ArtificialIntelligence.ShipWeight();
                            sw.ship = item1;
                            sw.weight = 3f;

                            this.NearbyShips.Add(sw);
                            this.BadGuysNear = true;
                            //this.PotentialTargets.Add(item1);
                        }
                        else if ((item1.loyalty != this.Owner.loyalty
                            && this.Owner.loyalty.GetRelations()[item1.loyalty].AtWar
                            || this.Owner.loyalty.isFaction || item1.loyalty.isFaction))//&& Vector2.Distance(this.Owner.Center, item.Center) < 15000f)
                        {
                            ArtificialIntelligence.ShipWeight sw = new ArtificialIntelligence.ShipWeight();
                            sw.ship = item1;
                            sw.weight = 1f;
                            this.NearbyShips.Add(sw);
                            //this.PotentialTargets.Add(item1);
                            this.BadGuysNear = Vector2.Distance(Position, item1.Position) <= Radius;
                        }


                    }
                }
                #endregion
            }
            //if target is !null but inactive
            //set inactive target to null; reset hasPriorityTarget boolean
            if (this.Target != null && !this.Target.Active) //What is the "Active" boolean used for?
            {
                this.Target = null;
                this.hasPriorityTarget = false;
            }
            //if target is !null, and active, and priority this ship has priority target
            else if (this.Target != null && this.Target.Active && this.hasPriorityTarget)
            {
                //if at war with target ship
                //this.Owner.loyalty.isFaction   : a hostile?
                if (this.Owner.loyalty.GetRelations()[(this.Target as Ship).loyalty].AtWar || this.Owner.loyalty.isFaction || (this.Target as Ship).loyalty.isFaction)
                {
                    //this.PotentialTargets.Add(this.Target as Ship); //why was this commented out? To be replaced with targetQueue?
                    this.TargetQueue.Add(this.Target as Ship); //added by Ermenildo V. Castro, Jr.
                                                               //07/28/15
                                                               //Perhaps unnecessary since the method returns this.Target
                                                               //Whichever invokes ScanForCombatTargets may already add this Target to the TargetQueue, therefore redundant method call.
                    this.BadGuysNear = true;
                }
                return this.Target;
            }
            if (this.Owner.GetHangars().Where(hangar => hangar.IsSupplyBay).Count() > 0 && this.Owner.engineState != Ship.MoveState.Warp && !this.Owner.isSpooling)
            #region supply ship logic
            {
                IOrderedEnumerable<Ship> sortedList = null;
                //if (this.Owner.Role == "station" || this.Owner.Role == "platform")
                //{
                //    sortedList = this.Owner.loyalty.GetShips().Where(ship => Vector2.Distance(this.Owner.Center, ship.Center) < 10 * this.Owner.SensorRange && ship != this.Owner && ship.engineState != Ship.MoveState.Warp && ship.Mothership == null && ship.OrdinanceMax > 0 && ship.Ordinance / ship.OrdinanceMax < .5 && !ship.IsTethered()).OrderBy(ship => ship.HasSupplyBays).ThenBy(ship => ship.OrdAddedPerSecond).ThenBy(ship => Math.Truncate((Vector2.Distance(this.Owner.Center, ship.Center) + 9999)) / 10000).ThenBy(ship => ship.OrdinanceMax - ship.Ordinance);
                //}
                //else
                {
                    sortedList = FriendliesNearby.Where(ship => ship != this.Owner 
                        && ship.engineState != Ship.MoveState.Warp 
                        && ship.Mothership == null 
                        && ship.OrdinanceMax > 0 
                        && ship.Ordinance / ship.OrdinanceMax < .5 
                        && !ship.IsTethered())
                        .OrderBy(ship => ship.HasSupplyBays).ThenBy(ship => ship.OrdAddedPerSecond).ThenBy(ship => Math.Truncate((Vector2.Distance(this.Owner.Center, ship.Center) + 4999)) / 5000).ThenBy(ship => ship.OrdinanceMax - ship.Ordinance);
                }

               
                    if (sortedList.Count() > 0)
                    {
                        int skip = 0;
                        float inboundOrdinance = 0f;
                        foreach (ShipModule hangar in this.Owner.GetHangars().Where(hangar => hangar.IsSupplyBay))
                        {
                            if (hangar.GetHangarShip() != null)
                            {
                                if (hangar.GetHangarShip().GetAI().State != AIState.Ferrying)
                                {
                                    if (sortedList.Skip(skip).Count() > 0)
                                    {
                                        ArtificialIntelligence.ShipGoal g1 = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.SupplyShip, Vector2.Zero, 0f);
                                        hangar.GetHangarShip().GetAI().EscortTarget = sortedList.Skip(skip).First();//.Where(ship => !ship.IsTethered()).FirstOrDefault();//sortedList.ElementAt<Ship>(y);

                                        hangar.GetHangarShip().GetAI().IgnoreCombat = true;
                                        hangar.GetHangarShip().GetAI().OrderQueue.Clear();
                                        hangar.GetHangarShip().GetAI().OrderQueue.AddLast(g1);
                                        hangar.GetHangarShip().GetAI().State = AIState.Ferrying;
                                        continue;
                                    }
                                    else
                                    {
                                        hangar.GetHangarShip().QueueTotalRemoval();
                                        continue;
                                    }
                                }
                                else if (sortedList.Skip(skip).Count() > 0 && hangar.GetHangarShip().GetAI().EscortTarget == sortedList.Skip(skip).First())
                                {
                                    inboundOrdinance = inboundOrdinance + 50f;
                                    if (inboundOrdinance + sortedList.Skip(skip).First().Ordinance / sortedList.First().OrdinanceMax > .5f)
                                    {
                                        skip++;
                                        inboundOrdinance = 0;
                                        continue;
                                    }
                                }
                                continue;
                            }
                            if (!hangar.Active || hangar.hangarTimer > 0f || this.Owner.Ordinance < 50f || sortedList.Skip(skip).Count() <= 0)
                            {
                                continue;
                            }
                            inboundOrdinance = inboundOrdinance + 50f;
                            Ship shuttle = ResourceManager.CreateShipFromHangar(this.Owner.loyalty.data.StartingScout, this.Owner.loyalty, this.Owner.Center, this.Owner);
                            shuttle.VanityName = "Resupply Shuttle";
                            shuttle.Role = "supply";
                            shuttle.GetAI().IgnoreCombat = true;
                            shuttle.GetAI().DefaultAIState = AIState.Flee;
                            Ship ship1 = shuttle;
                            randomThreadMath = (this.Owner.GetSystem() != null ? this.Owner.GetSystem().RNG : ArtificialIntelligence.universeScreen.DeepSpaceRNG);
                            ship1.Velocity = (randomThreadMath.RandomDirection() * shuttle.speed) + this.Owner.Velocity;
                            if (shuttle.Velocity.Length() > shuttle.velocityMaximum)
                            {
                                shuttle.Velocity = Vector2.Normalize(shuttle.Velocity) * shuttle.speed;
                            }
                            float ord_amt = 50f;
                            Ship owner = this.Owner;
                            owner.Ordinance = owner.Ordinance - 50f;
                            hangar.SetHangarShip(shuttle);
                            ArtificialIntelligence.ShipGoal g = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.SupplyShip, Vector2.Zero, 0f);
                            shuttle.GetAI().EscortTarget = sortedList.Skip(skip).First();
                            g.VariableNumber = ord_amt;
                            shuttle.GetAI().IgnoreCombat = true;
                            shuttle.GetAI().OrderQueue.Clear();
                            shuttle.GetAI().OrderQueue.AddLast(g);
                            shuttle.GetAI().State = AIState.Ferrying;
                            break;
                        }
                    }
    
            } 
            #endregion
            if (this.Owner.VanityName == "Resupply Shuttle" && this.Owner.Mothership == null)
            {
                {
                    this.Owner.QueueTotalRemoval();
                }
            }   
            //}           
            foreach (ArtificialIntelligence.ShipWeight nearbyShip in this.NearbyShips )
            //Parallel.ForEach(this.NearbyShips, nearbyShip =>
            {
                //opposition ship
                if (nearbyShip.ship.loyalty != this.Owner.loyalty)
                {
                    if (nearbyShip.ship.Weapons.Count ==0)
                    {
                        ArtificialIntelligence.ShipWeight vultureWeight = nearbyShip;
                        vultureWeight.weight = vultureWeight.weight + this.CombatAI.PirateWeight;
                    }
                    
                    if (nearbyShip.ship.Health / nearbyShip.ship.HealthMax < 0.5f)
                    {
                        ArtificialIntelligence.ShipWeight vultureWeight = nearbyShip;
                        vultureWeight.weight = vultureWeight.weight + this.CombatAI.VultureWeight;
                    }
                    if (nearbyShip.ship.Size < 30)
                    {
                        ArtificialIntelligence.ShipWeight smallAttackWeight = nearbyShip;
                        smallAttackWeight.weight = smallAttackWeight.weight + this.CombatAI.SmallAttackWeight;
                    }
                    if (nearbyShip.ship.Size > 30 && nearbyShip.ship.Size < 100)
                    {
                        ArtificialIntelligence.ShipWeight mediumAttackWeight = nearbyShip;
                        mediumAttackWeight.weight = mediumAttackWeight.weight + this.CombatAI.MediumAttackWeight;
                    }
                    if (nearbyShip.ship.Size > 100)
                    {
                        ArtificialIntelligence.ShipWeight largeAttackWeight = nearbyShip;
                        largeAttackWeight.weight = largeAttackWeight.weight + this.CombatAI.LargeAttackWeight;
                    }
                    float rangeToTarget = Vector2.Distance(nearbyShip.ship.Center, this.Owner.Center);
                    if (rangeToTarget <= this.CombatAI.PreferredEngagementDistance) 
                       // && Vector2.Distance(nearbyShip.ship.Center, this.Owner.Center) >= this.Owner.maxWeaponsRange)
                    {
                        ArtificialIntelligence.ShipWeight shipWeight = nearbyShip;
                        shipWeight.weight = shipWeight.weight + 2.5f;
                    }
                    else if (rangeToTarget > this.CombatAI.PreferredEngagementDistance)
                    {
                        ArtificialIntelligence.ShipWeight shipWeight1 = nearbyShip;
                        shipWeight1.weight = shipWeight1.weight - 2.5f *(rangeToTarget /(this.CombatAI.PreferredEngagementDistance+1));
                    }
                    if(nearbyShip.ship.Weapons.Count <1)
                    {
                        nearbyShip.weight -= 3;
                    }
                    
                    foreach (ArtificialIntelligence.ShipWeight otherShip in this.NearbyShips)
                    {
                        if (otherShip.ship.loyalty != this.Owner.loyalty)
                        {
                            if (otherShip.ship.GetAI().Target != this.Owner)
                            {
                                continue;
                            }
                            ArtificialIntelligence.ShipWeight selfDefenseWeight = nearbyShip;
                            selfDefenseWeight.weight = selfDefenseWeight.weight + 0.2f * this.CombatAI.SelfDefenseWeight;
                        }
                        else if (otherShip.ship.GetAI().Target != nearbyShip.ship)
                        {
                            continue;
                        }
                    }

                }
                else
                {
                    this.NearbyShips.QueuePendingRemoval(nearbyShip);
                }

            }
           //this.PotentialTargets = this.NearbyShips.Where(loyalty=> loyalty.ship.loyalty != this.Owner.loyalty) .OrderBy(weight => weight.weight).Select(ship => ship.ship).ToList();
            //if (this.Owner.Role == "platform")
            //{
            //    this.NearbyShips.ApplyPendingRemovals();
            //    IEnumerable<ArtificialIntelligence.ShipWeight> sortedList =
            //        from potentialTarget in this.NearbyShips
            //        orderby Vector2.Distance(this.Owner.Center, potentialTarget.ship.Center)
            //        select potentialTarget;
            //    if (sortedList.Count<ArtificialIntelligence.ShipWeight>() > 0)
            //    {
            //        this.Target = sortedList.ElementAt<ArtificialIntelligence.ShipWeight>(0).ship;
            //    }
            //    return this.Target;
            //}
            this.NearbyShips.ApplyPendingRemovals();
            IEnumerable<ArtificialIntelligence.ShipWeight> sortedList2 =
                from potentialTarget in this.NearbyShips
                orderby potentialTarget.weight descending
                select potentialTarget;
            this.PotentialTargets = sortedList2.Select(ship => ship.ship)
                .ToList();

            //trackprojectiles in scan for targets.
                        
            this.PotentialTargets = this.PotentialTargets.Where(potentialTarget => Vector2.Distance(potentialTarget.Center, this.Owner.Center) < this.CombatAI.PreferredEngagementDistance).ToList();
            if (sortedList2.Count<ArtificialIntelligence.ShipWeight>() > 0)
            {
                if (this.Owner.Role == "supply" && this.Owner.VanityName != "Resupply Shuttle")
                {
                    this.Target = sortedList2.ElementAt<ArtificialIntelligence.ShipWeight>(0).ship;
                }
                this.Target = sortedList2.ElementAt<ArtificialIntelligence.ShipWeight>(0).ship;
            }
            if (this.Owner.Weapons.Count > 0 || this.Owner.GetHangars().Count > 0)
                return this.Target;          
            return null;
        } //end ScanForTargets method

        private void SetCombatStatus(float elapsedTime)
        {
            //if(this.State==AIState.Scrap)
            //{
            //    this.Target = null;
            //    this.Owner.InCombatTimer = 0f;
            //    this.Owner.InCombat = false;
            //    this.TargetQueue.Clear();
            //    return;
                
            //}
            float radius = 30000f;
            Vector2 senseCenter = this.Owner.Center;
            if (UseSensorsForTargets)
            {
                if (this.Owner.Mothership != null)
                {
                    if (Vector2.Distance(this.Owner.Center, this.Owner.Mothership.Center) <= this.Owner.Mothership.SensorRange - this.Owner.SensorRange)
                    {
                        senseCenter = this.Owner.Mothership.Center;
                        radius = this.Owner.Mothership.SensorRange;
                    }
                }
                else
                {
                    radius = this.Owner.SensorRange;
                    if (this.Owner.inborders) radius += 10000;
                }
            }
            if (this.Owner.fleet != null)
            {
                if (!this.hasPriorityTarget)
                {
                    this.Target = this.ScanForCombatTargets(senseCenter, radius);
                }
                else
                {
                    this.ScanForCombatTargets(senseCenter, radius);
                }
            }
            else if (!this.hasPriorityTarget)
            {
//#if DEBUG
//                if (this.State == AIState.Intercept && this.Target != null)
//                    System.Diagnostics.Debug.WriteLine(this.Target); 
//#endif
                this.Target = this.ScanForCombatTargets(senseCenter, radius);
            }
            else
            {
                this.ScanForCombatTargets(senseCenter, radius);
            }
            if (this.State == AIState.Resupply)
            {
                return;
            }
            if (((this.Owner.Role == "freighter" && this.Owner.CargoSpace_Max > 0) || this.Owner.Role == "scout" || this.Owner.Role == "construction" || this.Owner.Role == "troop" || this.IgnoreCombat || this.State == AIState.Resupply || this.State == AIState.ReturnToHangar || this.State == AIState.Colonize) || this.Owner.VanityName == "Resupply Shuttle")
            {
                return;
            }
            if (this.Owner.fleet != null && this.State == AIState.FormationWarp)
            {
                bool doreturn = true;
                if (this.Owner.fleet != null && this.State == AIState.FormationWarp && Vector2.Distance(this.Owner.Center, this.Owner.fleet.Position + this.Owner.FleetOffset) < 15000f)
                {
                    doreturn = false;
                }
                if (doreturn)
                {
                    //if (this.Owner.engineState == Ship.MoveState.Sublight && this.NearbyShips.Count > 0)
                    //{
                    //    this.Owner.ShieldsUp = true;
                    //}
                    return;
                }
            }
            if (this.Owner.fleet != null)
            {
                foreach (FleetDataNode datanode in this.Owner.fleet.DataNodes)
                {
                    if (datanode.GetShip() != this.Owner)
                    {
                        continue;
                    }
                    this.node = datanode;
                    break;
                }
            }
            if (this.Target != null && !this.Owner.InCombat)
            {
                this.Owner.InCombatTimer = 15f;
                if (!this.HasPriorityOrder && this.OrderQueue.Count > 0 && this.OrderQueue.ElementAt<ArtificialIntelligence.ShipGoal>(0).Plan != ArtificialIntelligence.Plan.DoCombat)
                {
                    ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
                    this.State = AIState.Combat;
                    this.OrderQueue.AddFirst(combat);
                    return;
                }
                else if (!this.HasPriorityOrder)
                {
                    ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
                    this.State = AIState.Combat;
                    this.OrderQueue.AddFirst(combat);
                    return;
                }
                else 
                {
                    if (!this.HasPriorityOrder || this.CombatState == CombatState.HoldPosition || this.OrderQueue.Count != 0)
                        return;
                    ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
                    this.State = AIState.Combat;
                    this.OrderQueue.AddFirst(combat);
                    
                }
            }
        }

		private void ScrapShip(float elapsedTime, ArtificialIntelligence.ShipGoal goal)
		{
            if (Vector2.Distance(goal.TargetPlanet.Position, this.Owner.Center) >= 2500f +goal.TargetPlanet.ObjectRadius + this.Owner.Radius)   //2500f)   //OrbitTarget.ObjectRadius *15)
			{
                //goal.MovePosition = goal.TargetPlanet.Position;
                //this.MoveToWithin1000(elapsedTime, goal);
                //goal.SpeedLimit = this.Owner.GetSTLSpeed();
                this.DoOrbit(goal.TargetPlanet, elapsedTime);
				return;
			}
			this.OrderQueue.Clear();
			Planet targetPlanet = goal.TargetPlanet;
			targetPlanet.ProductionHere = targetPlanet.ProductionHere + this.Owner.GetCost(this.Owner.loyalty) / 2f;
			this.Owner.QueueTotalRemoval();
            this.Owner.loyalty.GetGSAI().recyclepool++;
		}

		private void SetCombatStatusorig(float elapsedTime)
		{
			if (this.Owner.fleet != null)
			{
				if (!this.hasPriorityTarget)
				{
					this.Target = this.ScanForCombatTargets(this.Owner.Center, 30000f);
				}
				else
				{
					this.ScanForCombatTargets(this.Owner.Center, 30000f);
				}
			}
			else if (!this.hasPriorityTarget)
			{
				this.Target = this.ScanForCombatTargets(this.Owner.Center, 30000f);
			}
			else
			{
				this.ScanForCombatTargets(this.Owner.Center, 30000f);
			}
			if (this.State == AIState.Resupply)
			{
				return;
			}
			if ((this.Owner.Role == "freighter" || this.Owner.Role == "scout" || this.Owner.Role == "construction" || this.Owner.Role == "troop" || this.IgnoreCombat || this.State == AIState.Resupply || this.State == AIState.ReturnToHangar) && !this.Owner.IsSupplyShip)
			{
				return;
			}
			if (this.Owner.fleet != null && this.State == AIState.FormationWarp)
			{
				bool doreturn = true;
				if (this.Owner.fleet != null && this.State == AIState.FormationWarp && Vector2.Distance(this.Owner.Center, this.Owner.fleet.Position + this.Owner.FleetOffset) < 15000f)
				{
					doreturn = false;
				}
				if (doreturn)
				{
					return;
				}
			}
			if (this.Owner.fleet != null)
			{
				foreach (FleetDataNode datanode in this.Owner.fleet.DataNodes)
				{
					if (datanode.GetShip() != this.Owner)
					{
						continue;
					}
					this.node = datanode;
					break;
				}
			}
			if (this.Target != null && !this.Owner.InCombat)
			{
				this.Owner.InCombat = true;
				this.Owner.InCombatTimer = 15f;
				if (!this.HasPriorityOrder && this.OrderQueue.Count > 0 && this.OrderQueue.ElementAt<ArtificialIntelligence.ShipGoal>(0).Plan != ArtificialIntelligence.Plan.DoCombat)
				{
					ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
					this.State = AIState.Combat;
					this.OrderQueue.AddFirst(combat);
					return;
				}
				if (!this.HasPriorityOrder)
				{
					ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
					this.State = AIState.Combat;
					this.OrderQueue.AddFirst(combat);
					return;
				}
				if (this.HasPriorityOrder && this.CombatState != Ship_Game.Gameplay.CombatState.HoldPosition && this.OrderQueue.Count == 0)
				{
					ArtificialIntelligence.ShipGoal combat = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f);
					this.State = AIState.Combat;
					this.OrderQueue.AddFirst(combat);
					return;
				}
			}
			else if (this.Target == null)
			{
				this.Owner.InCombat = false;
			}
		}

		public void SetPriorityOrder()
		{
			this.OrderQueue.Clear();
			this.HasPriorityOrder = true;
			this.Intercepting = false;
			this.hasPriorityTarget = false;
		}

		private void Stop(float elapsedTime)
		{
			this.Owner.HyperspaceReturn();
			if (this.Owner.Velocity == Vector2.Zero || this.Owner.Velocity.Length() > this.Owner.VelocityLast.Length())
			{
				this.Owner.Velocity = Vector2.Zero;
				return;
			}
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			if (this.Owner.Velocity.Length() / this.Owner.velocityMaximum <= elapsedTime || (forward.X <= 0f || this.Owner.Velocity.X <= 0f) && (forward.X >= 0f || this.Owner.Velocity.X >= 0f))
			{
				this.Owner.Velocity = Vector2.Zero;
				return;
			}
			Ship owner = this.Owner;
			owner.Velocity = owner.Velocity + (Vector2.Normalize(-forward) * (elapsedTime * this.Owner.velocityMaximum));
		}

		private void Stop(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
		{
			this.Owner.HyperspaceReturn();
			if (this.Owner.Velocity == Vector2.Zero || this.Owner.Velocity.Length() > this.Owner.VelocityLast.Length())
			{
				this.Owner.Velocity = Vector2.Zero;
				this.OrderQueue.RemoveFirst();
				return;
			}
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			if (this.Owner.Velocity.Length() / this.Owner.velocityMaximum <= elapsedTime || (forward.X <= 0f || this.Owner.Velocity.X <= 0f) && (forward.X >= 0f || this.Owner.Velocity.X >= 0f))
			{
				this.Owner.Velocity = Vector2.Zero;
				return;
			}
			Ship owner = this.Owner;
			owner.Velocity = owner.Velocity + (Vector2.Normalize(-forward) * (elapsedTime * this.Owner.velocityMaximum));
		}

		private void StopWithBackwardsThrust(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
		{
			if(Goal.TargetPlanet !=null)
            {
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Last().Equals(Goal.TargetPlanet.Position);
                    Goal.MovePosition = Goal.TargetPlanet.Position;
                }
            }
            if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
			{
				this.HadPO = true;
			}
			this.HasPriorityOrder = false;
			float Distance = Vector2.Distance(this.Owner.Center, Goal.MovePosition);
			if (Distance < 100f && Distance < 25f)
			{
				this.OrderQueue.RemoveFirst();
				lock (this.wayPointLocker)
				{
					this.ActiveWayPoints.Clear();
				}
				this.Owner.Velocity = Vector2.Zero;
				if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
				{
					this.HadPO = true;
				}
				this.HasPriorityOrder = false;
			}
			this.Owner.HyperspaceReturn();
            //Vector2 forward2 = Quaternion
            //Quaternion.AngleAxis(_angle, Vector3.forward) * normalizedDirection1
			Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
			if (this.Owner.Velocity == Vector2.Zero || Vector2.Distance(this.Owner.Center + (this.Owner.Velocity * elapsedTime), Goal.MovePosition) > Vector2.Distance(this.Owner.Center, Goal.MovePosition))
			{
				this.Owner.Velocity = Vector2.Zero;
				this.OrderQueue.RemoveFirst();
				if (this.ActiveWayPoints.Count > 0)
				{
					lock (this.wayPointLocker)
					{
						this.ActiveWayPoints.Dequeue();
					}
				}
				return;
			}
			Vector2 velocity = this.Owner.Velocity;
			float timetostop = velocity.Length() / Goal.SpeedLimit;
            //added by gremlin devekmod timetostopfix
            if (Vector2.Distance(this.Owner.Center, Goal.MovePosition) / Goal.SpeedLimit <= timetostop + .005) 
            //if (Vector2.Distance(this.Owner.Center, Goal.MovePosition) / (this.Owner.Velocity.Length() + 0.001f) <= timetostop)
			{
				Ship owner = this.Owner;
				owner.Velocity = owner.Velocity + (Vector2.Normalize(forward) * (elapsedTime * Goal.SpeedLimit));
				if (this.Owner.Velocity.Length() > Goal.SpeedLimit)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * Goal.SpeedLimit;
				}
			}
			else
			{
				Ship ship = this.Owner;
				ship.Velocity = ship.Velocity + (Vector2.Normalize(forward) * (elapsedTime * Goal.SpeedLimit));
				if (this.Owner.Velocity.Length() > Goal.SpeedLimit)
				{
					this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * Goal.SpeedLimit;
					return;
				}
			}
		}
        private void StopWithBackwardsThrustbroke(float elapsedTime, ArtificialIntelligence.ShipGoal Goal)
        {
            
            if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
            {
                this.HadPO = true;
            }
            this.HasPriorityOrder = false;
            float Distance = Vector2.Distance(this.Owner.Center, Goal.MovePosition);
            if (Distance < 200 )//&& Distance > 25f)
            {
                this.OrderQueue.RemoveFirst();
                lock (this.wayPointLocker)
                {
                    this.ActiveWayPoints.Clear();
                }
                this.Owner.Velocity = Vector2.Zero;
                if (this.Owner.loyalty == EmpireManager.GetEmpireByName(ArtificialIntelligence.universeScreen.PlayerLoyalty))
                {
                    this.HadPO = true;
                }
                this.HasPriorityOrder = false;
            }
            this.Owner.HyperspaceReturn();
            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
            if (this.Owner.Velocity == Vector2.Zero || Vector2.Distance(this.Owner.Center + ( this.Owner.Velocity * elapsedTime), Goal.MovePosition) > Vector2.Distance(this.Owner.Center, Goal.MovePosition))
            {
                this.Owner.Velocity = Vector2.Zero;
                this.OrderQueue.RemoveFirst();
                if (this.ActiveWayPoints.Count > 0)
                {
                    lock (this.wayPointLocker)
                    {
                        this.ActiveWayPoints.Dequeue();
                    }
                }
                return;
            }
            Vector2 velocity = this.Owner.Velocity;
            float timetostop = (int)velocity.Length() / Goal.SpeedLimit;
            if (Vector2.Distance(this.Owner.Center, Goal.MovePosition) / Goal.SpeedLimit <= timetostop + .005) //(this.Owner.Velocity.Length() + 1)
                if (Math.Abs((int)(DistanceLast - Distance)) < 10)
                {

                    ArtificialIntelligence.ShipGoal to1k = new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.MakeFinalApproach, Goal.MovePosition, 0f)
                                    {
                                        SpeedLimit = this.Owner.speed > Distance ? Distance : this.Owner.GetSTLSpeed()
                                    };
                    lock (this.wayPointLocker)
                        this.OrderQueue.AddFirst(to1k);
                    this.DistanceLast = Distance;
                    return;
                }
            if (Vector2.Distance(this.Owner.Center, Goal.MovePosition) / (this.Owner.Velocity.Length() + 0.001f) <= timetostop)
            {
                Ship owner = this.Owner;
                owner.Velocity = owner.Velocity + (Vector2.Normalize(-forward) * (elapsedTime * Goal.SpeedLimit));
                if (this.Owner.Velocity.Length() > Goal.SpeedLimit)
                {
                    this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * Goal.SpeedLimit;
                }
            }
            else
            {
                Ship ship = this.Owner;
                ship.Velocity = ship.Velocity + (Vector2.Normalize(forward) * (elapsedTime * Goal.SpeedLimit));
                if (this.Owner.Velocity.Length() > Goal.SpeedLimit)
                {
                    this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * Goal.SpeedLimit;
                    return;
                }
            }

            this.DistanceLast = Distance;
        }
		
        private void ThrustTowardsPosition(Vector2 Position, float elapsedTime, float speedLimit)
        {
            //if(speedLimit>1f)
            //speedLimit = (int)(speedLimit);
            if (speedLimit == 0f)
            {
                speedLimit = this.Owner.speed;
            }
            float Distance = Vector2.Distance(Position, this.Owner.Center);
 
            if (this.Owner.engineState != Ship.MoveState.Warp )
            {
                Position = Position - this.Owner.Velocity;
            }
            if (!this.Owner.EnginesKnockedOut)
            {
                this.Owner.isThrusting = true;

                Vector2 wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, Position));
                //wantedForward = Vector2.Normalize(wantedForward);
                Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
                //forward = Vector2.Normalize(forward);
                Vector2 right = new Vector2(-forward.Y, forward.X);
                //right = Vector2.Normalize(right);
                double angleDiff = Math.Acos((double)Vector2.Dot(wantedForward, forward));
                double facing = (Vector2.Dot(wantedForward, right)> 0f ? 1f : -1f);
                //facing = facing/(mag1*mag2);
                #region warp
                if (angleDiff > 0.25f && Distance > 2500f && this.Owner.engineState == Ship.MoveState.Warp)
                {
                    //this.Owner.speed *= 0.999f;
                    if (this.ActiveWayPoints.Count > 1)
                    {
                        wantedForward= Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, this.ActiveWayPoints.ElementAt<Vector2>(1)));
                        float angleDiffToNext = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
                        float d = Vector2.Distance(this.Owner.Position, this.ActiveWayPoints.ElementAt<Vector2>(1));
                        //if (d < 50000f)
                        if (d <= this.Owner.velocityMaximum)
                        {
                            if (angleDiffToNext > 0.4f)// 0.649999976158142) //  )
                            {
                                this.Owner.HyperspaceReturn();

                            }

                        }
                        else if (d > 50000f && angleDiffToNext > 1.64999997615814f)
                        //else if (d > this.Owner.GetFTLSpeed() && angleDiffToNext>1.65f)//*(d/this.Owner.speed ))
                        {
                            this.Owner.HyperspaceReturn();
                            

                        }
                    }
                    else if (this.Target != null)
                    {
                        float d = Vector2.Distance(this.Target.Center, this.Owner.Center);
                        if (angleDiff > 0.400000005960464f)
                        {
                            this.Owner.HyperspaceReturn();
                        }
                        else if (d > 25000f)
                        {
                            this.Owner.HyperspaceReturn();
                        }
                    }
                    else if ((this.State != AIState.Bombard && this.State!=AIState.AssaultPlanet && this.State != AIState.BombardTroops  && !this.IgnoreCombat) || this.OrderQueue.Count <= 0)
                    {
                        this.Owner.HyperspaceReturn();
                    }
                    else if (this.OrderQueue.Last<ArtificialIntelligence.ShipGoal>().TargetPlanet != null)
                    {
                        float d = Vector2.Distance(this.OrderQueue.Last<ArtificialIntelligence.ShipGoal>().TargetPlanet.Position, this.Owner.Center);
                        wantedForward = Vector2.Normalize(HelperFunctions.FindVectorToTarget(this.Owner.Center, this.OrderQueue.Last<ArtificialIntelligence.ShipGoal>().TargetPlanet.Position));
                         angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
                        //float d = Vector2.Distance(this.Owner.Position, this.ActiveWayPoints.ElementAt<Vector2>(1));
                        //if (angleDiff > 0.65f)

                        if (angleDiff > 0.400000005960464f)
                        {
                            this.Owner.HyperspaceReturn();
                        }
                        else if (d > 25000f)
                        {
                            this.Owner.HyperspaceReturn();
                        }
                    }
                    else if(angleDiff>.25)
                        this.Owner.HyperspaceReturn();
                }
                #endregion
                if (this.hasPriorityTarget && Distance < this.Owner.maxWeaponsRange)
                {
                    if (this.Owner.engineState == Ship.MoveState.Warp)
                    {
                        this.Owner.HyperspaceReturn();
                    }
                }
                else if (!this.HasPriorityOrder && !this.hasPriorityTarget && Distance < 1000f && this.ActiveWayPoints.Count <= 1 && this.Owner.engineState == Ship.MoveState.Warp)
                {
                    this.Owner.HyperspaceReturn();
                }
                //if (angleDiff > 0.125000000372529f)
                //    if (this.Owner.engineState == Ship.MoveState.Warp)
                //        this.Owner.speed *= .8f;
                float TurnSpeed = 1;
                if (angleDiff > this.Owner.yBankAmount*.1) // this.Owner.rotationRadiansPerSecond *elapsedTime*.1 ) //0.025000000372529f )//*
                {

                    double RotAmount = Math.Min(angleDiff, facing *  this.Owner.yBankAmount); //this.Owner.rotationRadiansPerSecond * elapsedTime);
                    //RotAmount *= facing;
                    if (RotAmount > 0f)
                    {
                        
                        if (this.Owner.yRotation > -this.Owner.maxBank)
                        {
                            
                            Ship owner = this.Owner;
                            owner.yRotation = owner.yRotation - this.Owner.yBankAmount;
                        }
                    }
                    else if (RotAmount < 0f && this.Owner.yRotation < this.Owner.maxBank)
                    {
                        
                        Ship owner1 = this.Owner;
                        owner1.yRotation = owner1.yRotation + this.Owner.yBankAmount;
                        
                    }

                    this.Owner.isTurning = true;
                    Ship rotation = this.Owner;
                    rotation.Rotation = rotation.Rotation + (RotAmount > angleDiff ? (float)angleDiff: (float)RotAmount);

                    //if (RotAmount > .05f)
                    {
                        float nimble = this.Owner.rotationRadiansPerSecond;// >1?1:this.Owner.rotationRadiansPerSecond;
                        if (angleDiff < nimble)
                            TurnSpeed = (float)((nimble*1.5 -angleDiff )/(nimble*1.5));     //(float)RotAmount / (this.Owner.rotationRadiansPerSecond * elapsedTime);

                        else
                            return;
                    }

                   
                }
                if (this.State != AIState.FormationWarp || this.Owner.fleet == null)
                {
                    if (Distance > 7500f && !this.Owner.InCombat && angleDiff < 0.25f)
                    {
                        this.Owner.EngageStarDrive();
                    }
                    else if (Distance > 15000f && this.Owner.InCombat && angleDiff < 0.25f)
                    {

                        this.Owner.EngageStarDrive();
                    }
                    if (this.Owner.engineState == Ship.MoveState.Warp)
                    {
                        if (angleDiff > .1f)
                        {

                            speedLimit = (this.Owner.speed); // this.Owner.speed;
                        }
                        else
                            speedLimit = (int)(this.Owner.velocityMaximum);
                    }
                    else if (Distance > this.Owner.speed * 10f)
                    {
                        //if (angleDiff > .1f)
                        //    speedLimit = this.Owner.speed;
                        //else
                        speedLimit = (this.Owner.speed);
                    }
                    speedLimit *= TurnSpeed;
                    Ship velocity = this.Owner;
                    velocity.Velocity = velocity.Velocity +   (Vector2.Normalize(forward) * (elapsedTime * speedLimit));//((forward) * (elapsedTime * speedLimit));
                    if (this.Owner.Velocity.Length() > speedLimit)
                    {
                        this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * speedLimit; //(this.Owner.Velocity) * speedLimit; //
                    }

                }
                else
                {
                    if (Distance > 7500f)
                    //if(Distance > this.Owner.speed -1000)
                    {
                        bool fleetReady = true;
                        this.Owner.fleet.Ships.thisLock.EnterReadLock();
                        foreach (Ship ship in this.Owner.fleet.Ships)
                        {
                            if(ship.GetAI().State != AIState.FormationWarp)
                                continue;
                            if (ship.GetAI().ReadyToWarp
                                
                                && (ship.PowerCurrent / (ship.PowerStoreMax + 0.01f) >= 0.2f || ship.isSpooling ) 
                                
                                
                                )
                            {
                                if (this.Owner.FightersOut)
                                    this.Owner.RecoverFighters();
                                
                                continue;
                            }

                            fleetReady = false;
                            break;
                        }
                        this.Owner.fleet.Ships.thisLock.ExitReadLock();
     
                            float distanceFleetCenterToDistance = this.Owner.fleet.StoredFleetDistancetoMove; //
                            speedLimit = (this.Owner.fleet.speed);

                            #region FleetGrouping


                            float fleetPosistionDistance = Distance;// Vector2.Distance(this.Owner.Center, Position);
                            if (fleetPosistionDistance <= distanceFleetCenterToDistance )
                            {
                                float speedreduction = distanceFleetCenterToDistance - Distance;
                                speedLimit = (int)( this.Owner.fleet.speed - speedreduction); //this.Owner.fleet.speed 
                                if (speedLimit < 0)//this.Owner.fleet.speed * .25f
                                    speedLimit = 0; //this.Owner.fleet.speed * .25f;
                                else if (speedLimit > this.Owner.fleet.speed)
                                    speedLimit = (int)(this.Owner.fleet.speed);
                            }
                            //else if (Distance > distanceFleetCenterToDistance) //radius * 4f
                            else if (fleetPosistionDistance > distanceFleetCenterToDistance && Distance >this.Owner.speed)
                            {

                                float speedIncrease = Distance - distanceFleetCenterToDistance ;
                                //distanceShipToFleetCenter > this.Owner.fleet.speed && 
                                speedLimit = (int)(this.Owner.fleet.speed + speedIncrease);
  
                            }
                            //if (Distance < speedimit)
                            //    speedLimit = Distance;



                            #endregion

                        
                        

                        if (fleetReady)
                        {
                            this.Owner.EngageStarDrive();
                        }
                        else if (this.Owner.engineState == Ship.MoveState.Warp)
                        {
                            this.Owner.HyperspaceReturn();
                        }
                    }
                    else if (this.Owner.engineState == Ship.MoveState.Warp)
                    {
                        this.Owner.HyperspaceReturn();
                    }
           

                    if (speedLimit > this.Owner.velocityMaximum)
                        speedLimit = (this.Owner.velocityMaximum);
                    else if (speedLimit < 0)
                        speedLimit = 0;
                    //if (Distance < 100000)
                    //    speedLimit *= .5f;
                    Ship velocity1 = this.Owner;
                    velocity1.Velocity = velocity1.Velocity + (Vector2.Normalize(forward) * (elapsedTime * (speedLimit)));
                    if (this.Owner.Velocity.Length() > speedLimit)
                    {
                        this.Owner.Velocity = Vector2.Normalize(this.Owner.Velocity) * speedLimit;
                        return;
                    }
                }
            }
        }



        //added by gremlin Devekmod AuUpdate(fixed)
        public void Update(float elapsedTime)
        {
            this.CombatAI.UpdateCombatAI(this.Owner);
            ArtificialIntelligence.ShipGoal toEvaluate;
            if (this.State == AIState.AwaitingOrders && this.DefaultAIState == AIState.Exterminate)
            {
                this.State = AIState.Exterminate;
            }
            if (this.Owner.Name == "Subspace Projector")
            {
                this.BadGuysNear = true;
                return;
            }
            if (this.ClearOrdersNext)
            {
                this.OrderQueue.Clear();
                this.ClearOrdersNext = false;
                this.State = AIState.AwaitingOrders;
            }
            List<Ship> ToRemove = new List<Ship>();
            foreach (Ship target in this.TargetQueue)
            {
                if (target.Active)
                {
                    continue;
                }
                ToRemove.Add(target);
            }
            foreach (Ship ship in ToRemove)
            {
                this.TargetQueue.Remove(ship);
            }
            if (!this.hasPriorityTarget)
            {
                this.TargetQueue.Clear();
            }
            if (this.Owner.loyalty == ArtificialIntelligence.universeScreen.player && (  this.State == AIState.MoveTo && Vector2.Distance(this.Owner.Center, this.MovePosition) > 100f || this.State == AIState.Orbit || (this.State == AIState.Bombard || this.State == AIState.AssaultPlanet || this.State == AIState.BombardTroops) || this.State == AIState.Rebase || this.State == AIState.Scrap || this.State == AIState.Resupply || this.State == AIState.Refit || this.State == AIState.FormationWarp))
            {
                this.HasPriorityOrder = true;
            }
            if (this.State == AIState.Resupply)
            {
                this.HasPriorityOrder = true;
                if (this.Owner.Ordinance >= this.Owner.OrdinanceMax)
                {
                    this.HasPriorityOrder = false;
                }
            }
            //if (this.State == AIState.Flee && !this.BadGuysNear)// Vector2.Distance(this.OrbitTarget.Position, this.Owner.Position) < this.Owner.SensorRange + 10000)
            //{
            //    if(this.OrderQueue.Count > 0)
            //        this.OrderQueue.Remove(this.OrderQueue.Last);
            //    if (this.Owner.CargoSpace_Used > 0)
            //        this.State = AIState.SystemTrader;
            //    else
            //        this.State = this.DefaultAIState;
            //}
            this.ScanForThreatTimer -=  elapsedTime;
            if (this.ScanForThreatTimer < 0f)
            {
                if (this.inOrbit == true )//&& !(this.State == AIState.Orbit ||                     this.State == AIState.Flee))
                {
                    this.inOrbit = false;
                }
                this.SetCombatStatus(elapsedTime);
                this.ScanForThreatTimer = 2f;
                if (this.Owner.loyalty.data.Traits.Pack)
                {
                    this.Owner.DamageModifier = -0.25f;
                    Ship owner = this.Owner;
                    owner.DamageModifier = owner.DamageModifier + 0.05f * (float)this.FriendliesNearby.Count;
                    if (this.Owner.DamageModifier > 0.5f)
                    {
                        this.Owner.DamageModifier = 0.5f;
                    }
                }
            }
            this.UtilityModuleCheckTimer -= elapsedTime;
            if (this.UtilityModuleCheckTimer <= 0f)
            {
                this.UtilityModuleCheckTimer = 1f;
                //Added by McShooterz: logic for transporter moduels
                if (this.Owner.hasTransporter)
                {
                    foreach (ShipModule module in this.Owner.Transporters)
                    {
                        if (module.TransporterTimer <= 0f && module.Active && module.Powered && module.TransporterPower < this.Owner.PowerCurrent)
                        {
                            if (this.FriendliesNearby.Count > 0 && module.TransporterOrdnance > 0 && this.Owner.Ordinance > 0)
                                this.DoOrdinanceTransporterLogic(module);
                            if (module.TransporterTroopAssault > 0 && this.Owner.TroopList.Count() > 0)
                                this.DoAssaultTransporterLogic(module);
                        }
                    }
                }
                //Do repair check if friendly ships around and no combat
                if (!this.Owner.InCombat && this.FriendliesNearby.Count > 0)
                {
                    //Added by McShooterz: logic for repair beams
                    if (this.Owner.hasRepairBeam)
                    {
                        foreach (ShipModule module in this.Owner.RepairBeams)
                        {
                            if (module.InstalledWeapon.timeToNextFire <= 0f && module.InstalledWeapon.moduleAttachedTo.Powered && this.Owner.Ordinance >= module.InstalledWeapon.OrdinanceRequiredToFire && this.Owner.PowerCurrent >= module.InstalledWeapon.PowerRequiredToFire)
                            {
                                this.DoRepairBeamLogic(module.InstalledWeapon);
                            }
                        }
                    }
                    if (this.Owner.HasRepairModule)
                    {
                        foreach (Weapon weapon in this.Owner.Weapons)
                        {
                            if (weapon.timeToNextFire > 0f || !weapon.moduleAttachedTo.Powered || this.Owner.Ordinance < weapon.OrdinanceRequiredToFire || this.Owner.PowerCurrent < weapon.PowerRequiredToFire || !weapon.IsRepairDrone)
                            {
                                continue;
                            }
                            this.DoRepairDroneLogic(weapon);
                        }
                    }
                }
            }
            if (this.State == AIState.ManualControl)
            {
                return;
            }
            this.ReadyToWarp = true;
            this.Owner.isThrusting = false;
            this.Owner.isTurning = false;

            
            #region old flee code
            //if (!this.HasPriorityOrder 
            //    && (this.BadGuysNear || this.Owner.InCombat) 
            //    && (this.Owner.shipData == null || this.Owner.shipData.ShipCategory == ShipData.Category.Civilian) 
            //    && this.Owner.Weapons.Count == 0 && this.Owner.GetHangars().Count == 0 && this.Owner.Transporters.Count() == 0
            //    && (this.Owner.Role !="troop" 
            //    && this.Owner.Role != "construction" 
            //    && this.State !=AIState.Colonize 
            //    && !this.IgnoreCombat && this.State != AIState.Rebase) 
            //    && (this.Owner.Role == "freighter" || this.Owner.fleet == null || this.Owner.Mothership != null))
            //{
            //    if (this.State != AIState.Flee )
            //    {
            //        this.HasPriorityOrder = true;
            //        this.State = AIState.Flee;
            //        if (this.State == AIState.Flee)
            //        {
            //            this.OrderFlee(false);
            //            this.Owner.InCombatTimer = 15f;

            //        }
            //    }
            //    else if (this.State == AIState.Flee && (this.OrbitTarget != null && Vector2.Distance(this.OrbitTarget.Position, this.Owner.Position) < this.Owner.SensorRange + 10000))
            //    {
            //        this.State = this.DefaultAIState;
            //    }
            //} 
            #endregion
            if (this.State == AIState.SystemTrader && this.start != null && this.end != null && (this.start.Owner != this.Owner.loyalty || this.end.Owner != this.Owner.loyalty))
            {
                this.start = null;
                this.end = null;
                this.OrderTrade();
                return;
            }
            if (this.State == AIState.PassengerTransport && this.start != null && this.end != null && (this.start.Owner != this.Owner.loyalty || this.end.Owner != this.Owner.loyalty))
            {
                this.start = null;
                this.end = null;
                this.OrderTransportPassengers();
                return;
            }
#if !DEBUG
            try
            {
#endif
                if (this.OrderQueue.Count == 0)
                {
                    if (this.Owner.fleet == null)
                    {
                        lock (this.wayPointLocker)
                        {
                            this.ActiveWayPoints.Clear();
                        }
                        AIState state = this.State;
                        if (state <= AIState.MoveTo)
                        {
                            if (state <= AIState.SystemTrader)
                            {
                                if (state == AIState.DoNothing)
                                {
                                    this.AwaitOrders(elapsedTime);
                                }
                                else
                                {
                                    switch (state)
                                    {
                                        case AIState.AwaitingOrders:
                                            {
                                                if (this.Owner.loyalty != ArtificialIntelligence.universeScreen.player)
                                                {
                                                    this.AwaitOrders(elapsedTime);
                                                }
                                                else
                                                {
                                                    this.AwaitOrdersPlayer(elapsedTime);
                                                }
                                                if (this.Owner.OrdinanceMax == 0 || this.Owner.OrdinanceMax > 0 && this.Owner.Ordinance / this.Owner.OrdinanceMax >= 0.2f)
                                                {

                                                    break;


                                                }
                                                if (FriendliesNearby.Where(supply => supply.HasSupplyBays && supply.Ordinance >= 100).Count() > 0)
                                                {
                                                    break;
                                                }
                                                List<Planet> shipyards = new List<Planet>();
                                                for (int i = 0; i < this.Owner.loyalty.GetPlanets().Count; i++)
                                                {
                                                    Planet item = this.Owner.loyalty.GetPlanets()[i];
                                                    if (item.HasShipyard)
                                                    {
                                                        shipyards.Add(item);
                                                    }
                                                }
                                                IOrderedEnumerable<Planet> sortedList =
                                                    from p in shipyards
                                                    orderby Vector2.Distance(this.Owner.Center, p.Position)
                                                    select p;
                                                if (sortedList.Count<Planet>() <= 0)
                                                {
                                                    break;
                                                }
                                                this.OrderResupply(sortedList.First<Planet>(), true);
                                                break;
                                            }
                                        case AIState.Escort:
                                            {
                                                if (this.EscortTarget != this.Owner.Mothership || this.Owner.Mothership == null || !this.Owner.Mothership.InCombat)
                                                {
                                                    if (this.EscortTarget == null || !this.EscortTarget.Active)
                                                    {
                                                        this.State = AIState.AwaitingOrders;   //fbedard
                                                        break;
                                                    }
                                                    this.OrbitShip(this.EscortTarget, elapsedTime);
                                                    break;
                                                }
                                                else
                                                {
                                                    if(this.Target == null && !(this.hasPriorityTarget || this.HasPriorityOrder))
                                                    this.Target = this.Owner.Mothership.GetAI().Target;
                                                    this.DoCombat(elapsedTime);
                                                    break;
                                                }
                                            }
                                        case AIState.SystemTrader:
                                            {
                                                this.OrderTrade();
                                                if (this.end != null && this.start != null)
                                                {
                                                    break;
                                                }
                                                this.AwaitOrders(elapsedTime);
                                                break;
                                            }
                                    }
                                }
                            }
                            else if (state == AIState.PassengerTransport)
                            {
                                this.OrderTransportPassengers();
                                if (this.end == null || this.start == null)
                                {
                                    this.AwaitOrders(elapsedTime);
                                }
                            }
                            else if (state != AIState.MoveTo)
                            {
                            }
                        }
                        else if (state <= AIState.ReturnToHangar)
                        {
                            switch (state)
                            {
                                case AIState.SystemDefender:
                                    {
                                        if (this.Target != null)
                                        System.Diagnostics.Debug.WriteLine("SD Tatget" + this.Owner.VanityName);
                                        //if(this.Target == null)
                                        this.AwaitOrders(elapsedTime);
                                        break;
                                    }
                                case AIState.AwaitingOffenseOrders:
                                    {
                                        break;
                                    }
                                case AIState.Resupply:
                                    {
                                        if (this.Owner.Ordinance != this.Owner.OrdinanceMax)
                                        {
                                            break;
                                        }
                                        this.State = AIState.AwaitingOrders;
                                        break;
                                    }
                                default:
                                    {
                                        if (state == AIState.ReturnToHangar)
                                        {
                                            this.DoReturnToHangar(elapsedTime);
                                            break;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                            }
                        }
                        else if (state != AIState.Intercept)
                        {
                            if (state == AIState.Exterminate)
                            {
                                this.OrderFindExterminationTarget(true);
                            }
                        }
                        else if (this.Target != null)
                        {
                            this.OrbitShip(this.Target as Ship, elapsedTime);
                        }
                    }
                    else
                    {
                        float DistanceToFleetOffset = Vector2.Distance(this.Owner.Center, this.Owner.fleet.Position + this.Owner.FleetOffset);
                        Vector2 toAdd = (this.Owner.Velocity != Vector2.Zero ? Vector2.Normalize(this.Owner.Velocity) : Vector2.Zero) * 100f;
                        Vector2.Distance(this.Owner.Center, (this.Owner.fleet.Position + this.Owner.FleetOffset) + toAdd);
                        Vector2 vector2 = HelperFunctions.findPointFromAngleAndDistanceUsingRadians(this.Owner.fleet.Position + this.Owner.FleetOffset, this.Owner.fleet.facing, 1f);
                        Vector2 fvec = HelperFunctions.FindVectorToTarget(Vector2.Zero, vector2);
                        if (DistanceToFleetOffset <= 75f || this.HasPriorityOrder)
                        {
                            this.Owner.Velocity = Vector2.Zero;
                            vector2 = HelperFunctions.findPointFromAngleAndDistanceUsingRadians(Vector2.Zero, this.Owner.fleet.facing, 1f);
                            fvec = HelperFunctions.FindVectorToTarget(Vector2.Zero, vector2);
                            Vector2 wantedForward = Vector2.Normalize(fvec);
                            Vector2 forward = new Vector2((float)Math.Sin((double)this.Owner.Rotation), -(float)Math.Cos((double)this.Owner.Rotation));
                            Vector2 right = new Vector2(-forward.Y, forward.X);
                            float angleDiff = (float)Math.Acos((double)Vector2.Dot(wantedForward, forward));
                            float facing = (Vector2.Dot(wantedForward, right) > 0f ? 1f : -1f);
                            if (angleDiff > 0.02f)
                            {
                                this.RotateToFacing(elapsedTime, angleDiff, facing);
                            }
                            this.State = AIState.AwaitingOrders;
                        }
                        else
                        {
                            this.ThrustTowardsPosition(this.Owner.fleet.Position + this.Owner.FleetOffset, elapsedTime, this.Owner.fleet.speed);
                            lock (this.wayPointLocker)
                            {
                                this.ActiveWayPoints.Clear();
                                this.ActiveWayPoints.Enqueue(this.Owner.fleet.Position + this.Owner.FleetOffset);
                                if (this.Owner.fleet.GetStack().Count > 0)
                                {
                                    this.ActiveWayPoints.Enqueue(this.Owner.fleet.GetStack().Peek().MovePosition + this.Owner.FleetOffset);
                                }
                            }
                        }
                    }
                }
                else if (this.OrderQueue.Count > 0)
                {
#if DEBUG
                    try
#endif
                    {
                        toEvaluate = this.OrderQueue.First<ArtificialIntelligence.ShipGoal>();
                    }
#if DEBUG                    
                    catch

                    {
                        return;
                    }
#endif
                    Planet target = toEvaluate.TargetPlanet;
                    switch (toEvaluate.Plan)
                    {
                        case ArtificialIntelligence.Plan.HoldPosition:
                            {
                                //
 
                                //
                                this.HoldPosition();


                                
                                break;
                            }
                        case ArtificialIntelligence.Plan.Stop:
                            {
                                this.Stop(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Scrap:
                            {
                                this.ScrapShip(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Bombard:
                            //target = toEvaluate.TargetPlanet; //Modified by Ermenildo V. Castro, Jr.; 07/29/15: Removed redunant statement, assignment occurs before SWITCH
                            if (this.Owner.Ordinance < 0.05* this.Owner.OrdinanceMax
                                || (target.TroopsHere.Count <= target.TroopsHere.Count * .40f  || target.Population <= target.Population * .10f) //Legacy Code: (target.BuildingList.Count == 0 && target.TroopsHere.Count == 0  && target.Population <0f)
                                                                                                                              //Modified by Ermenildo V. Castro, Jr. for Issue #488
                                                                                                                              //Issue #488:
                                                                                                                              //The ai needs to soften planets more for invasion but try not to kill all the populace
                                                                                                                              //Modified conditional to reflect goals for Issue #488:
                                                                                                                              //        - Stop bombarding if planet population is less than 10% of the original target population
                                                                                                                              //        - Stop bombarding if opposition troops are attrited by at least 60%, id est: target troops less than 40% of original troop count
                                || target.GetGroundStrengthOther(this.Owner.loyalty) * 1.5
                                <= target.GetGroundStrength(this.Owner.loyalty)
                                )
                            {
                                this.OrderQueue.Clear();
                                if(this.CombatState == CombatState.Evade)
                                this.State = AIState.AwaitingOrders;
                                this.HasPriorityOrder = false;
                            }
                            this.DoOrbit(toEvaluate.TargetPlanet, elapsedTime);
                            float radius = toEvaluate.TargetPlanet.ObjectRadius + this.Owner.Radius + 1000;
                            if (toEvaluate.TargetPlanet.Owner == this.Owner.loyalty) //cannot bombard friednly planets
                            {
                                this.OrderQueue.Clear();
                                return;
                            }
                            else if (Vector2.Distance(this.Owner.Center, toEvaluate.TargetPlanet.Position) < radius)                                
                            {                                
                                using (List<ShipModule>.Enumerator enumerator = this.Owner.BombBays.GetEnumerator())
                                {
                                    while (enumerator.MoveNext())
                                    {
                                        ShipModule current = enumerator.Current;
                                        if ((double)current.BombTimer <= 0.0)
                                        {
                                            Bomb bomb = new Bomb(new Vector3(this.Owner.Center, 0.0f), this.Owner.loyalty);
                                            bomb.WeaponName = current.BombType;
                                            if ((double)this.Owner.Ordinance > (double)ResourceManager.WeaponsDict[current.BombType].OrdinanceRequiredToFire)
                                            {
                                                this.Owner.Ordinance -= ResourceManager.WeaponsDict[current.BombType].OrdinanceRequiredToFire;
                                                bomb.SetTarget(toEvaluate.TargetPlanet);
                                                //lock (GlobalStats.BombLock)
                                                    ArtificialIntelligence.universeScreen.BombList.Add(bomb);
                                                current.BombTimer = ResourceManager.WeaponsDict[current.BombType].fireDelay;
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                            else
                                break;
                        case ArtificialIntelligence.Plan.BombTroops:
                            target = toEvaluate.TargetPlanet;

                            if (target.TroopsHere.Where(unfriendlyTroops => unfriendlyTroops.GetOwner() != this.Owner.loyalty).Count() * 1.5
                                >= target.TilesList.Sum(space => space.number_allowed_troops))
                            {
                                if ((double)this.Owner.Ordinance < 0.0500000007450581 * (double)this.Owner.OrdinanceMax)
                                {
                                    this.OrderQueue.Clear();
                                    this.State = AIState.AwaitingOrders;
                                    this.HasPriorityOrder = false;
                                }
                                this.DoOrbit(toEvaluate.TargetPlanet, elapsedTime);
                                 radius = toEvaluate.TargetPlanet.ObjectRadius + this.Owner.Radius + 1000;
                                if (toEvaluate.TargetPlanet.Owner == this.Owner.loyalty)

                                {
                                    this.OrderQueue.Clear();
                                    return;
                                }                                    
                                else if ((double)Vector2.Distance(this.Owner.Center, toEvaluate.TargetPlanet.Position) < radius)
                                {
                                    using (List<ShipModule>.Enumerator enumerator = this.Owner.BombBays.GetEnumerator())
                                    {
                                        while (enumerator.MoveNext())
                                        {
                                            ShipModule current = enumerator.Current;
                                            if ((double)current.BombTimer <= 0.0)
                                            {
                                                Bomb bomb = new Bomb(new Vector3(this.Owner.Center, 0.0f), this.Owner.loyalty);
                                                bomb.WeaponName = current.BombType;
                                                if ((double)this.Owner.Ordinance > (double)ResourceManager.WeaponsDict[current.BombType].OrdinanceRequiredToFire)
                                                {
                                                    this.Owner.Ordinance -= ResourceManager.WeaponsDict[current.BombType].OrdinanceRequiredToFire;
                                                    bomb.SetTarget(toEvaluate.TargetPlanet);
                                                    //lock (GlobalStats.BombLock)
                                                        ArtificialIntelligence.universeScreen.BombList.Add(bomb);
                                                    current.BombTimer = ResourceManager.WeaponsDict[current.BombType].fireDelay;
                                                }
                                            }
                                        }
                                        break;
                                    }
                                }
                                else
                                    break;
                            }
                            else if (this.Owner.HasTroopBay || (this.Owner.hasTransporter))
                            {
                                this.State = AIState.AssaultPlanet;
                                this.OrderAssaultPlanet(target);
                            }
                            else
                                this.OrderQueue.Clear();
                            break;

                        case ArtificialIntelligence.Plan.Exterminate:
                            {
                                this.DoOrbit(toEvaluate.TargetPlanet, elapsedTime);
                                radius = toEvaluate.TargetPlanet.ObjectRadius + this.Owner.Radius + 1000;
                                if (toEvaluate.TargetPlanet.Owner == this.Owner.loyalty || toEvaluate.TargetPlanet.Owner == null)
                                {
                                    this.OrderQueue.Clear();
                                    this.OrderFindExterminationTarget(true);
                                    return;
                                }
                                else
                                {
                                    if (Vector2.Distance(this.Owner.Center, toEvaluate.TargetPlanet.Position) >= radius)
                                        break;
                                    List<ShipModule>.Enumerator enumerator1 = this.Owner.BombBays.GetEnumerator();
                                    try
                                    {
                                        while (enumerator1.MoveNext())
                                        {
                                            ShipModule mod = enumerator1.Current;
                                            if (mod.BombTimer > 0f)
                                                continue;
                                            Bomb b = new Bomb(new Vector3(this.Owner.Center, 0f), this.Owner.loyalty)
                                            {
                                                WeaponName = mod.BombType
                                            };
                                            if (this.Owner.Ordinance <= ResourceManager.WeaponsDict[mod.BombType].OrdinanceRequiredToFire)
                                            {
                                                continue;
                                            }
                                            Ship owner1 = this.Owner;
                                            owner1.Ordinance = owner1.Ordinance - ResourceManager.WeaponsDict[mod.BombType].OrdinanceRequiredToFire;
                                            b.SetTarget(toEvaluate.TargetPlanet);
                                            //lock (GlobalStats.BombLock)
                                            {
                                                ArtificialIntelligence.universeScreen.BombList.Add(b);
                                            }
                                            mod.BombTimer = ResourceManager.WeaponsDict[mod.BombType].fireDelay;
                                        }
                                        break;
                                    }
                                    finally
                                    {
                                        ((IDisposable)enumerator1).Dispose();
                                    }
                                }
                            }
                        case ArtificialIntelligence.Plan.RotateToFaceMovePosition:
                            {
                                this.RotateToFaceMovePosition(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.RotateToDesiredFacing:
                            {
                                this.RotateToDesiredFacing(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.MoveToWithin1000:
                            {
                                this.MoveToWithin1000(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.MakeFinalApproachFleet:
                            {
                                if (this.Owner.fleet != null)
                                {
                                    this.MakeFinalApproachFleet(elapsedTime, toEvaluate);
                                    break;
                                }
                                else
                                {
                                    this.State = AIState.AwaitingOrders;
                                    break;
                                }
                            }
                        case ArtificialIntelligence.Plan.MoveToWithin1000Fleet:
                            {
                                if (this.Owner.fleet != null)
                                {
                                    this.MoveToWithin1000Fleet(elapsedTime, toEvaluate);
                                    break;
                                }
                                else
                                {
                                    this.State = AIState.AwaitingOrders;
                                    break;
                                }
                            }
                        case ArtificialIntelligence.Plan.MakeFinalApproach:
                            {
                                this.MakeFinalApproach(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.RotateInlineWithVelocity:
                            {
                                this.RotateInLineWithVelocity(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.StopWithBackThrust:
                            {
                                this.StopWithBackwardsThrust(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Orbit:
                            {
                                this.DoOrbit(toEvaluate.TargetPlanet, elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Colonize:
                            {
                                this.Colonize(toEvaluate.TargetPlanet);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Explore:
                            {
                                this.DoExplore(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Rebase:
                            {
                                this.DoRebase(toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.DefendSystem:
                            {
                                if (this.Target != null)
                                    System.Diagnostics.Debug.WriteLine(this.Target);
                                this.DoSystemDefense(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.DoCombat:
                            {
                                this.DoCombat(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.MoveTowards:
                            {
                                this.MoveTowardsPosition(this.MovePosition, elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.PickupPassengers:
                            {
                                if (this.start != null)
                                    this.PickupPassengers();
                                else
                                    this.State = AIState.AwaitingOrders;
                                break;
                            }
                        case ArtificialIntelligence.Plan.DropoffPassengers:
                            {

                                try
                                {
                                    this.DropoffPassengers();
                                }
                                catch 
                                {
                                    System.Diagnostics.Debug.WriteLine("DropoffPassengers failed");

                                }
                                break;
                            }
                        case ArtificialIntelligence.Plan.DeployStructure:
                            {
                                this.DoDeploy(toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.PickupGoods:
                            {
                                try
                                {
                                    this.PickupGoods();
                                }
                                catch
                                { }
                                break;
                            }
                        case ArtificialIntelligence.Plan.DropOffGoods:
                            {
                                this.DropoffGoods();
                                break;
                            }
                        case ArtificialIntelligence.Plan.ReturnToHangar:
                            {
                                this.DoReturnToHangar(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.TroopToShip:
                            {
                                this.DoTroopToShip(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.BoardShip:
                            {
                                this.DoBoardShip(elapsedTime);
                                break;
                            }
                        case ArtificialIntelligence.Plan.SupplyShip:
                            {
                                this.DoSupplyShip(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.Refit:
                            {
                                this.DoRefit(elapsedTime, toEvaluate);
                                break;
                            }
                        case ArtificialIntelligence.Plan.LandTroop:
                            {
                                this.DoLandTroop(elapsedTime, toEvaluate);
                                break;
                            }
                    }
                }
                goto Label0;
#if !DEBUG   
        }
            
            catch

            {
            }
            #endif
        Label0:
            AIState aIState = this.State;
            if (aIState == AIState.SystemTrader)
            {
                foreach (ArtificialIntelligence.ShipGoal goal in this.OrderQueue)
                {
                    

                    
                    if (goal.Plan == ArtificialIntelligence.Plan.TransportPassengers && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan == ArtificialIntelligence.Plan.PickupPassengers && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan == ArtificialIntelligence.Plan.PickupGoods && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan != ArtificialIntelligence.Plan.DropoffPassengers || goal.TargetPlanet == null || goal.TargetPlanet.Owner == this.Owner.loyalty)
                    {
                        if (goal.Plan != ArtificialIntelligence.Plan.DropOffGoods || goal.TargetPlanet == null || goal.TargetPlanet.Owner == this.Owner.loyalty)
                        {
                            continue;
                        }
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                }
            }
            else if (aIState == AIState.PassengerTransport)
            {
                foreach (ArtificialIntelligence.ShipGoal goal in this.OrderQueue)
                {
                    if (goal.Plan == ArtificialIntelligence.Plan.TransportPassengers && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan == ArtificialIntelligence.Plan.PickupPassengers && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan == ArtificialIntelligence.Plan.PickupGoods && goal.TargetPlanet != null && goal.TargetPlanet.Owner != this.Owner.loyalty)
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else if (goal.Plan != ArtificialIntelligence.Plan.DropoffPassengers || goal.TargetPlanet == null || goal.TargetPlanet.Owner == this.Owner.loyalty)
                    {
                        if (goal.Plan != ArtificialIntelligence.Plan.DropOffGoods || goal.TargetPlanet == null || goal.TargetPlanet.Owner == this.Owner.loyalty)
                        {
                            continue;
                        }
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                    else
                    {
                        this.OrderQueue.Clear();
                        this.State = AIState.AwaitingOrders;
                        break;
                    }
                }
            }
            else if (aIState == AIState.Rebase)
            {
                foreach (ArtificialIntelligence.ShipGoal goal in this.OrderQueue)
                //Parallel.ForEach(this.OrderQueue, (goal, state) =>
                {
                    if (goal.Plan != ArtificialIntelligence.Plan.Rebase || goal.TargetPlanet == null || goal.TargetPlanet.Owner == this.Owner.loyalty)
                    {
                        continue;
                    }
                    this.OrderQueue.Clear();
                    this.State = AIState.AwaitingOrders;
                    break;
                }
            }
            //if(targetChangeTimer >-1)
            //targetChangeTimer -= elapsedTime;
            //if(TriggerDelay >-1)
            TriggerDelay -= elapsedTime;
            if (this.Owner.InCombat && this.BadGuysNear && !this.IgnoreCombat)
            {
                if (this.TriggerDelay < 0)
                {
                    TriggerDelay = elapsedTime * 2;
                    bool docombat = false;
                    LinkedListNode<ArtificialIntelligence.ShipGoal> tempShipGoal = this.OrderQueue.First;
                    ShipGoal firstgoal = tempShipGoal != null ? tempShipGoal.Value : null;  //.FirstOrDefault<ArtificialIntelligence.ShipGoal>();
                    if (this.Owner.Weapons.Count > 0 || this.Owner.GetHangars().Count > 0)
#if !DEBUG
                        try
#endif
                    {



                        docombat = (!this.HasPriorityOrder && (this.OrderQueue.Count == 0 || firstgoal != null && firstgoal.Plan != ArtificialIntelligence.Plan.DoCombat));


                        if (docombat)//|| this.OrderQueue.Count == 0))
                        {
                            this.OrderQueue.AddFirst(new ArtificialIntelligence.ShipGoal(ArtificialIntelligence.Plan.DoCombat, Vector2.Zero, 0f));
                        }


                        //this.fireTask = Task.Factory.StartNew(this.FireOnTarget);//,TaskCreationOptions.LongRunning);
                        //fireTask = new Task(this.FireOnTarget);                    
                        this.FireOnTarget();


                    }

#if !DEBUG
                        catch
                        {
                        }
                
#endif
                }
            }
            else
            {
                if (this.Owner.GetHangars().Count > 0 && this.Owner.loyalty != ArtificialIntelligence.universeScreen.player)
                {
                    foreach (ShipModule hangar in this.Owner.GetHangars())
                    {
                        if (hangar.IsTroopBay || hangar.IsSupplyBay || hangar.GetHangarShip() == null
                            //||hangar.GetHangarShip().InCombat
                            || hangar.GetHangarShip().GetAI().State == AIState.ReturnToHangar)
                        {
                            continue;
                        }
                        hangar.GetHangarShip().GetAI().OrderReturnToHangar();
                    }
                }
                else if (this.Owner.GetHangars().Count > 0)
                {
                    foreach (ShipModule hangar in this.Owner.GetHangars())
                    {
                        if (hangar.IsTroopBay
                            || hangar.IsSupplyBay
                            || hangar.GetHangarShip() == null
                            || hangar.GetHangarShip().GetAI().State == AIState.ReturnToHangar
                            //|| hangar.GetHangarShip().InCombat
                            || hangar.GetHangarShip().GetAI().hasPriorityTarget
                            || hangar.GetHangarShip().GetAI().HasPriorityOrder

                            )
                        {
                            continue;
                        }
                        hangar.GetHangarShip().DoEscort(this.Owner);
                    }
                }
            }
            if (this.State == AIState.Resupply && !this.HasPriorityOrder)
            {
                this.HasPriorityOrder = true;
            }
            //if (this.Owner.Weapons.Count() == 0 && this.BadGuysNear) //||( this.State != AIState.AssaultPlanet && this.State != AIState.Boarding
            //{
            //    this.CombatState = Gameplay.CombatState.Evade;
            //}
            if (!this.Owner.isTurning)
            {
                this.DeRotate();
                return;
            }
            else
            {
                return;
            }
        }

		private static float WrapAngle(float radians)
		{
			while (radians < -3.14159274f)
			{
				radians = radians + 6.28318548f;
			}
			while (radians > 3.14159274f)
			{
				radians = radians - 6.28318548f;
			}
			return radians;
		}

		public enum Plan
		{
			Stop,
			Scrap,
			HoldPosition,
			Bombard,
			Exterminate,
			RotateToFaceMovePosition,
			RotateToDesiredFacing,
			MoveToWithin1000,
			MakeFinalApproachFleet,
			MoveToWithin1000Fleet,
			MakeFinalApproach,
			RotateInlineWithVelocity,
			StopWithBackThrust,
			Orbit,
			Colonize,
			Explore,
			Rebase,
			DoCombat,
			MoveTowards,
			Trade,
			DefendSystem,
			TransportPassengers,
			PickupPassengers,
			DropoffPassengers,
			DeployStructure,
			PickupGoods,
			DropOffGoods,
			ReturnToHangar,
			TroopToShip,
			BoardShip,
			SupplyShip,
			Refit,
			LandTroop,
			MoveToWithin7500,
            BombTroops
		}

		public class ShipGoal
		{
			public ArtificialIntelligence.Plan Plan;

			public Goal goal;

			public float VariableNumber;

			public string VariableString;

			public Fleet fleet;

			public Vector2 MovePosition;

			public float DesiredFacing;

			public float FacingVector;

			public Planet TargetPlanet;

			public float SpeedLimit = 1f;

			public ShipGoal(ArtificialIntelligence.Plan p, Vector2 pos, float facing)
			{
				this.Plan = p;
				this.MovePosition = pos;
				this.DesiredFacing = facing;
			}
		}

		public class ShipWeight
		{
			public Ship ship;

			public float weight;
            public bool defendEscort;

			public ShipWeight()
			{
			}
		}
        public class WayPoints
        {
            public Planet planet { get; set; }

            public Ship ship { get; set; }
            
            public Vector2 location { get ; set; }
        }
		private enum transportState
		{
			ChoosePickup,
			GoToPickup,
			ChooseDropDestination,
			GotoDrop,
			DoDrop
		}

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ArtificialIntelligence() { Dispose(false); }

        protected void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (this.NearbyShips != null)
                        this.NearbyShips.Dispose();
                    if (this.FriendliesNearby != null)
                        this.FriendliesNearby.Dispose();

                }
                this.NearbyShips = null;
                this.FriendliesNearby = null;
                this.disposed = true;
            }
        }
    }

}