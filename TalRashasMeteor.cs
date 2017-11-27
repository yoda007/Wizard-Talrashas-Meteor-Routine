using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using Trinity.Framework;
using Trinity.Framework.Helpers;
using Trinity.Components.Combat;
using Trinity.Components.Combat.Resources;
using Trinity.DbProvider;
using Trinity.Framework.Actors.ActorTypes;
using Trinity.Framework.Avoidance.Structures;
using Trinity.Framework.Objects;
using Trinity.Framework.Reference;
using Trinity.Framework.Actors.Attributes;
using Trinity.UI;
using Trinity.Settings;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Game.Internals.Actors;
using Trinity.Framework.Avoidance;
using Trinity.Framework.Grid;
using Trinity.Components.Coroutines;
using Buddy.Coroutines;


namespace Trinity.Routines.Wizard
{
	public sealed class TalRashasMeteor : WizardBase, IRoutine
	{

		#region Definition

		public string DisplayName => "膜法师站撸型塔陨v1.23";
		public string Description => "2.6.1站撸型塔陨";
		public string Author => "r0";
		public string Version => "1.23";
		public string Url => "http://www.demonbuddy.cn/thread-14875-1-1.html";

		#region Build Definition
		public Build BuildRequirements =>  new Build
		{
			Sets = new Dictionary<Set, SetBonus>
			{
				{ Sets.TalRashasElements, SetBonus.Third },
			},
			Skills = new Dictionary<Skill, Rune>
			{
				{ Skills.Wizard.FrostNova, Runes.Wizard.FrozenStorm },
				{ Skills.Wizard.Familiar, Runes.Wizard.Arcanot },
				{ Skills.Wizard.ArcaneTorrent, Runes.Wizard.StaticDischarge },
				{ Skills.Wizard.Meteor, Runes.Wizard.MeteorShower },
				{ Skills.Wizard.StormArmor, Runes.Wizard.PowerOfTheStorm },
				{ Skills.Wizard.Teleport, Runes.Wizard.Calamity }
			},
			Items = new List<Item>
			{
				Legendary.Deathwish,
				Legendary.EtchedSigil
			},
		};

		#endregion

		private static List<TrinityActor> ObjectCache => Core.Targets.Entries;
		public override KiteMode KiteMode => KiteMode.Never;
		public override float KiteDistance => 5f;

		public int CastDistanceNephalemRift => Settings.CastDistanceNephalemRift;
		public int CastDistanceGreaterRift => Settings.CastDistanceGreaterRift;

		public int CastDistance => Core.Rift.IsNephalemRift ? CastDistanceNephalemRift : CastDistanceGreaterRift;

		private TrinityPower NewArcaneTorrent(TrinityActor target)
			=> new TrinityPower(Skills.Wizard.ArcaneTorrent, 60f, target.AcdId, 25, 50);
        private TrinityPower NewMeteor(TrinityActor target)
            => new TrinityPower(Skills.Wizard.Meteor, 60f, target.AcdId);

        private bool _g_isAvoiding = true;
        private bool weaponHasChanged = true;
        private bool needToCheckSkills = true;
		private double lastChangeTime = 0;
        private double timeForCheckSkills = 60*1000;

		private int ArcaneTorrentRuneIndex = 9;
		private int ArcaneTorrentSlot = 0;
		private int MeteorRuneIndex = 9;
		private int MeteorSlot = 0;

		private double MyGetTickCount()
        {
            TimeSpan current = DateTime.Now - (new DateTime(1970, 1, 1, 0, 0, 0));
            return current.TotalMilliseconds;
        }

		private static TrinityActor BestEliteInRange(Vector3 pos, float range = 60f, bool canRayWalk = false, TrinityActor exclude = null)
		{
			return (from u in ObjectCache
				where u.IsUnit && u.IsValid && u.Weight > 0 &&
					((u.IsElite && u.EliteType != EliteTypes.Minion) || u.ActorSnoId == 360636 || u.IsTreasureGoblin) &&
					!u.IsSafeSpot && u != exclude &&
					u.Type != TrinityObjectType.Shrine &&
					u.Type != TrinityObjectType.ProgressionGlobe &&
					u.Type != TrinityObjectType.HealthGlobe &&
					u.ActorSnoId != 454066 &&
					Core.Grids.CanRayCast(pos, u.Position) &&
					(!canRayWalk || (canRayWalk && Core.Grids.CanRayWalk(pos, u.Position))) &&
					u.Position.Distance(pos) <= range
				orderby
					u.Position.Distance(pos),
					u.HitPointsPct descending
				select u).FirstOrDefault();
		}
		private TrinityActor BestClusterUnit(Vector3 pos, float range = 60f, bool canRayWalk = false, TrinityActor exclude = null)
		{
			return (from u in ObjectCache
				where u.IsUnit && u.IsValid && u.Weight > 0 && !u.IsSafeSpot &&
					u != exclude &&
					u.Type != TrinityObjectType.Shrine &&
					u.Type != TrinityObjectType.ProgressionGlobe &&
					u.Type != TrinityObjectType.HealthGlobe &&
					u.ActorSnoId != 454066 &&
					Core.Grids.CanRayCast(pos, u.Position) &&
					(!canRayWalk || (canRayWalk && Core.Grids.CanRayWalk(pos, u.Position))) &&
					u.Position.Distance(pos) <= range
				orderby
					u.NearbyUnitsWithinDistance(ClusterRadius) descending,
					u.Position.Distance(pos),
					u.HitPointsPct descending
				select u).FirstOrDefault();
		}
		private TrinityActor GetClosestUnitUnSafe(float maxDistance = 65f)
		{
			var result =
				(from u in ObjectCache
				where
					u.IsUnit && u.IsValid && u.Weight > 0 && u.RadiusDistance <= maxDistance &&
					u.Type != TrinityObjectType.Shrine &&
					u.Type != TrinityObjectType.ProgressionGlobe &&
					u.Type != TrinityObjectType.HealthGlobe &&
					u.ActorSnoId != 454066
				orderby
					u.Distance
				select u).FirstOrDefault();

			return result;
		}
		private TrinityActor ClosestTarget2(Vector3 pos, float range = 60f, bool canRayWalk = false, TrinityActor exclude = null)
        {
			return (from u in ObjectCache
                where u.IsUnit && u.IsValid && u.Weight > 0 && !u.IsSafeSpot &&
                    u != exclude &&
                    u.Type != TrinityObjectType.Shrine &&
                    u.Type != TrinityObjectType.ProgressionGlobe &&
                    u.Type != TrinityObjectType.HealthGlobe &&
                    u.ActorSnoId != 454066 &&
                    Core.Grids.CanRayCast(pos, u.Position) &&
                    (!canRayWalk || (canRayWalk && Core.Grids.CanRayWalk(pos, u.Position))) &&
					u.Position.Distance(pos) <= range
                orderby
					u.Position.Distance(pos),
					u.HitPointsPct descending
                select u).FirstOrDefault();
        }
		private TrinityActor GetBestTarget(Vector3 pos, float range = 60f, bool canRayWalk = true, TrinityActor exclude = null)
		{
			return BestEliteInRange(pos, range, canRayWalk, exclude) ?? BestClusterUnit(pos, range, canRayWalk, exclude);
		}

		private static bool IsValidTarget(TrinityActor target)
        {
            if (target == null)
                return false;

            if (target.AcdId == 322194)
                return true;

            if (!(target.IsUnit && target.IsValid && target.Weight > 0))
                return false;

            if (target.Type == TrinityObjectType.Shrine ||
                target.Type == TrinityObjectType.ProgressionGlobe ||
                target.Type == TrinityObjectType.HealthGlobe ||
                target.ActorSnoId == 454066)
                return false;

            return true;
        }

        private bool GetOculusPosition(out Vector3 position, float range, Vector3 fromLocation)
        {
            position = Vector3.Zero;

            TrinityActor actor =
                (from u in TargetUtil.SafeList(false)
                 where fromLocation.Distance2D(u.Position) <= range &&
                       u.ActorSnoId == 433966
                 orderby u.Distance
                 select u).ToList().FirstOrDefault();

            if (actor == null)
                return false;

            position = actor.Position;
            position.Z = Player.Position.Z;

            return true;
        }
        private bool TryGoOcculus(out TrinityPower power)
        {
            power = null;

            if (!Core.Rift.IsGreaterRift)
                return false;

            if (!IsInCombat)
                return false;

			if (_g_isAvoiding)
				return false;

            if (!CanTeleport && IsStuck)
                return false;

            Vector3 occulusPos = Vector3.Zero;
			if (!GetOculusPosition(out occulusPos, 58f, Player.Position))
				return false;

            if (occulusPos == Vector3.Zero)
                return false;

            float distance = occulusPos.Distance(Player.Position);

			if (Core.Rift.IsGaurdianSpawned && CurrentTarget != null && CurrentTarget.IsBoss && occulusPos.Distance(CurrentTarget.Position) < 15f)
				return false;


            if (Core.Avoidance.InCriticalAvoidance(occulusPos) && !Core.Buffs.HasInvulnerableShrine)
            {
                return false;
            }

            TrinityActor target = ClosestTarget2(occulusPos, CastDistance);
            if (!IsValidTarget(target))
            {
				if (DebugMode)
					Core.Logger.Warn($"神目周围{CastDistance}码内没有可以攻击的怪物，放弃踩神目");
                return false;
            }

            if (distance < 9f)
            {
				if (DebugMode)
					Core.Logger.Warn($"已经在神目中，无需移动");
                return false;
            }
            else if (CanTeleport)
            {
				if (DebugMode)
					Core.Logger.Warn($"飞向神目");
                power = Teleport(occulusPos);
            }
            else if (distance < 25f && Core.Buffs.HasBuff(74499))
            {
                Vector3 closePos = MathEx.CalculatePointFrom(occulusPos, Player.Position, 10f);
                if (Core.Grids.CanRayWalk(Player.Position, closePos))
                {
					if (DebugMode)
						Core.Logger.Warn($"向神目步行移动，距离我{distance}");
                    power = Walk(occulusPos);
                }
                else
                {
					if (DebugMode)
						Core.Logger.Warn($"神目不能直线到达，放弃");
                    return false;
                }
            }
            else
            {
				if (DebugMode)
					Core.Logger.Warn($"神目距离{distance}不合适，放弃");

                return false;
            }

            return power != null;
        }

        private void GetSkillInfo(SNOPower snoPower)
		{
			bool isOverrideActive = false;
            try
            {
                isOverrideActive = ZetaDia.Me.SkillOverrideActive;
            }
            catch (ArgumentException ex)
            {
            }

			var cPlayer = ZetaDia.Storage.PlayerDataManager.ActivePlayerData;
			for (int i = 0; i <= 5; i++)
            {
                var diaActiveSkill = cPlayer.GetActiveSkillByIndex(i, isOverrideActive);
                if (diaActiveSkill == null || diaActiveSkill.Power == SNOPower.None)
                    continue;

				if (diaActiveSkill.Power == snoPower)
				{
					if (snoPower == SNOPower.Wizard_ArcaneTorrent)
					{
						ArcaneTorrentRuneIndex = diaActiveSkill.RuneIndex;
						ArcaneTorrentSlot = i;
					}
					else if (snoPower == SNOPower.Wizard_Meteor)
					{
						MeteorRuneIndex = diaActiveSkill.RuneIndex;
						MeteorSlot = i;
					}
				}
            }
		}

		private void CheckWeaponAndSkills(bool initialization = false)
		{

			bool hasChecked = false;

			if (Settings.AutoChangeWeapon)
			{
				bool isNephalemRift = Core.Rift.IsNephalemRift && !Legendary.AetherWalker.IsEquipped && !Player.IsInTown;
				bool isGreaterRift = Player.IsInTown && !Legendary.Deathwish.IsEquipped && (!Core.Rift.IsGreaterRift
									|| Core.Rift.RiftComplete);
				if (isGreaterRift)
				{
					double lastTime = MyGetTickCount() - lastChangeTime;
					if (lastTime > timeForCheckSkills || weaponHasChanged)
					{
						weaponHasChanged = false;
						hasChecked = true;
						if (DebugMode)
							Core.Logger.Warn("检查武器配置.大米");
						var DSS = InventoryManager.Backpack.Select(CachedACDItem.GetTrinityItem).Where(i => i.AcdItem.IsValid && i.IsEquipment && i.IsUsableByClass(Core.Player.ActorClass) && !i.IsUnidentified && i.ActorSnoId == 331908);
						if (DSS != null)
						{
							weaponHasChanged = true;
							foreach (var ds in DSS)
							{
								InventoryManager.EquipItem(ds.AcdItem.AnnId, InventorySlot.LeftHand);
								Thread.Sleep(2000);
								Core.Logger.Warn("{0} ({1}) ({2}) was equipped", ds.AcdItem.Name, ds.AcdItem.ActorSnoId, ds.AcdItem.AnnId);
								break;
							}
						}
					}
				}
				else if (isNephalemRift)
				{
					double lastTime = MyGetTickCount() - lastChangeTime;
					if (lastTime > timeForCheckSkills || weaponHasChanged)
					{
						weaponHasChanged = false;
						hasChecked = true;
						if (DebugMode)
							Core.Logger.Warn("检查武器配置.小米");
						var AWS = InventoryManager.Backpack.Select(CachedACDItem.GetTrinityItem).Where(i => i.AcdItem.IsValid && i.IsEquipment && i.IsUsableByClass(Core.Player.ActorClass) && !i.IsUnidentified && i.ActorSnoId == 403781);
						if (AWS != null)
						{
							weaponHasChanged = true;
							foreach (var aw in AWS)
							{
								InventoryManager.EquipItem(aw.AcdItem.AnnId, InventorySlot.LeftHand);
								Thread.Sleep(2000);
								Core.Logger.Warn("{0} ({1}) ({2}) was equipped", aw.AcdItem.Name, aw.AcdItem.ActorSnoId, aw.AcdItem.AnnId);
								break;
							}
						}
					}
				}
			}
			if (Settings.AutoChangeSkills)
			{
				if (initialization)
				{
					GetSkillInfo(SNOPower.Wizard_ArcaneTorrent);
					GetSkillInfo(SNOPower.Wizard_Meteor);
				}

				if (!ZetaDia.Me.IsInCombat)
				{

					bool isNephalemRift = Core.Rift.IsNephalemRift && !Player.IsInTown;
					bool isGreaterRift = Player.IsInTown && (!Core.Rift.IsGreaterRift || Core.Rift.RiftComplete);

					//RuneIndex:
					//FlameWard = 0, ThunderCrash = 4
					//StaticDischarge = 3, MeteorShower = 1

					if (isGreaterRift)
					{
						double lastTime = MyGetTickCount() - lastChangeTime;
						if (lastTime > timeForCheckSkills || hasChecked || initialization)
						{
							hasChecked = true;
							if (DebugMode)
								Core.Logger.Warn($"检查技能配置.大米：ATRuneIndex{ArcaneTorrentRuneIndex} MRuneIndex{MeteorRuneIndex}");
							if (ArcaneTorrentRuneIndex != 3)
							{
								ArcaneTorrentRuneIndex = 3;
								ZetaDia.Me.SetActiveSkill(Skills.Wizard.ArcaneTorrent.SNOPower, 3, (HotbarSlot)ArcaneTorrentSlot);
								Thread.Sleep(1000);
								Core.Logger.Warn($"在城镇中，换成电奔");

							}
							if (MeteorRuneIndex != 1)
							{
								MeteorRuneIndex = 1;
								ZetaDia.Me.SetActiveSkill(Skills.Wizard.Meteor.SNOPower, 1, (HotbarSlot)MeteorSlot);
								Thread.Sleep(1000);
								Core.Logger.Warn($"在城镇中，换成火陨");

							}
						}
					}
					else if (isNephalemRift)
					{
						double lastTime = MyGetTickCount() - lastChangeTime;
						if (lastTime > timeForCheckSkills || hasChecked || initialization)
						{
							hasChecked = true;
							if (DebugMode)
								Core.Logger.Warn($"检查技能配置. 小米：ATRuneIndex{ArcaneTorrentRuneIndex} MRuneIndex{MeteorRuneIndex}");
							if (Skills.Wizard.ArcaneTorrent.CanCast() && ArcaneTorrentRuneIndex != 0)
							{
								ArcaneTorrentRuneIndex = 0;
								ZetaDia.Me.SetActiveSkill(Skills.Wizard.ArcaneTorrent.SNOPower, 0, (HotbarSlot)ArcaneTorrentSlot);
								Thread.Sleep(1000);
								Core.Logger.Warn($"在小米中，换成火奔");
							}
							if (Skills.Wizard.Meteor.CanCast() && MeteorRuneIndex != 4)
							{
								MeteorRuneIndex = 4;
								ZetaDia.Me.SetActiveSkill(Skills.Wizard.Meteor.SNOPower, 4, (HotbarSlot)MeteorSlot);
								Thread.Sleep(1000);
								Core.Logger.Warn($"在小米中，换成电陨");
							}
						}
					}
				}
			}
			if (hasChecked) lastChangeTime = MyGetTickCount();
		}

		protected bool TryTeleportMovement(Vector3 destination, out TrinityPower trinityPower)
		{
			trinityPower = null;

			if (!Skills.Wizard.Teleport.CanCast())
				return false;

			var path = Core.DBNavProvider.CurrentPath;
			if (path != null && path.Contains(destination))
			{
				var projectedPosition = IsBlocked
					? Core.Grids.Avoidance.GetPathCastPosition(50f, true)
					: Core.Grids.Avoidance.GetPathWalkPosition(50f, true);

				if (projectedPosition != Vector3.Zero)
				{
					var distance = projectedPosition.Distance(Player.Position);
					var inFacingDirection = Core.Grids.Avoidance.IsInPlayerFacingDirection(projectedPosition, 90);
					var TPRecastDelayMs = Legendary.AetherWalker.IsEquipped ? 1500 : 200;
					if ((distance > 15f || IsBlocked && distance > 5f) && inFacingDirection && Skills.Wizard.Teleport.TimeSinceUse > TPRecastDelayMs)
					{
						trinityPower = Teleport(projectedPosition);
						return true;
					}
				}
			}
			return false;
		}

		TrinityPower ToDestInPath(Vector3 dest)
		{
			TrinityPower power;

			if (!Core.Rift.IsGreaterRift || !TargetUtil.AnyMobsInRangeOfPosition(dest, 20f))
			{
				if (TryTeleportMovement(dest, out power))
					return power;
			}

			return ToDestByWalk(dest);
		}

		TrinityPower ToDestByWalk(Vector3 dest)
		{
			if (dest.Distance(Player.Position) > 10f && Core.Rift.IsGreaterRift)
				return Walk(dest, 10f);
			else
				return Walk(dest);
		}

		#endregion

		public TrinityPower GetOffensivePower()   //战斗函数
		{

			TrinityActor target;
			TrinityPower power;
			_g_isAvoiding = false;

			if (CurrentTarget != null && CurrentTarget.IsTreasureGoblin)
				return ArcaneTorrent(CurrentTarget);

			if (Core.Rift.IsNephalemRift)
				target = BestEliteInRange(Player.Position, 60f, true) ?? BestClusterUnit(Player.Position, CastDistance, true);
			else
			{
				if (IsBlocked)
					target = GetClosestUnitUnSafe(30) ?? GetBestTarget(Player.Position, CastDistance, true) ?? CurrentTarget;
				else
					target = GetBestTarget(Player.Position, CastDistance, true) ?? GetClosestUnitUnSafe(30) ?? CurrentTarget;
			}

			if (target == null)
				return null;

			if (!Core.Buffs.HasInvulnerableShrine)
			{
				bool needToAvoid = !DotNotAvoidWhenNephalemRift || !Core.Rift.IsNephalemRift;
				if (Core.Avoidance.InCriticalAvoidance(Player.Position) && needToAvoid)
				{
					var safeSpot = Vector3.Zero;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 20, 50, Player.Position) && safeSpot != Vector3.Zero)
					{
						if (CanTeleport)
						{
							if (DebugMode)
								Core.Logger.Log("躲避传送");
							return Teleport(safeSpot);
						}
						else if (!IsBlocked)
						{
							if (DebugMode)
								Core.Logger.Log("躲避");
							_g_isAvoiding = true;
							return ToDestByWalk(safeSpot);
						}
					}
				}
				if (target.IsBoss && target.Distance < 15 && needToAvoid)
				{
					var safeSpot = Vector3.Zero;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 25, 50, target.Position) && safeSpot != Vector3.Zero)
					{
						if (CanTeleport)
						{
							if (DebugMode)
								Core.Logger.Log("Boss传送");
							return Teleport(safeSpot);
						}
						else if (!IsBlocked)
						{
							if (DebugMode)
								Core.Logger.Log("躲避Boss");
							_g_isAvoiding = true;
							return ToDestByWalk(safeSpot);
						}
					}
				}
				if (!Core.Buffs.HasBuff(74499))
				{
					var safeSpot = Vector3.Zero;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 25, 50, target.Position) && safeSpot != Vector3.Zero)
					{
						if (CanTeleport)
						{
							if (DebugMode)
								Core.Logger.Log("电甲传送");
							return Teleport(safeSpot);
						}
						else if (!IsBlocked)
						{
							if (DebugMode)
								Core.Logger.Log("电甲躲避");
							_g_isAvoiding = true;
							return ToDestByWalk(safeSpot);
						}
					}
				}
				if (Player.CurrentHealthPct < SuperEmergencyHealthPct && GetClosestUnitUnSafe(15f) != null && needToAvoid)
				{
					var safeSpot = Vector3.Zero;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 30, 50, Player.Position) && safeSpot != Vector3.Zero)
					{
						if (CanTeleport)
						{
							if (DebugMode)
								Core.Logger.Log("危险血量");
							return Teleport(safeSpot);
						}
						else if (!IsBlocked)
						{
							if (DebugMode)
								Core.Logger.Log("危险血量2");
							_g_isAvoiding = true;
							return ToDestByWalk(safeSpot);
						}
					}
				}
			}

			if (TryGoOcculus(out power))
				return power;

			if (Skills.Wizard.Meteor.CanCast() && Player.PrimaryResource > 60.0 && Skills.Wizard.Meteor.TimeSinceUse > 3000.0)
				return NewMeteor(target);

			if (Skills.Wizard.ArcaneTorrent.CanCast() && Player.PrimaryResource > 16.0)
				return NewArcaneTorrent(target);

			return null;

		}

		public TrinityPower GetDefensivePower()
		{
			return null;
		}

		public TrinityPower GetBuffPower()
		{

			CheckWeaponAndSkills(lastChangeTime == 0);

			if (Player.IsInTown)
				return null;

			if (!Skills.Wizard.EnergyArmor.IsBuffActive && Skills.Wizard.EnergyArmor.CanCast())
				return EnergyArmor();

			if (!Skills.Wizard.MagicWeapon.IsBuffActive && Skills.Wizard.MagicWeapon.CanCast())
				return MagicWeapon();

			if (Skills.Wizard.FrostNova.CanCast())
				return FrostNova();

			if (!Skills.Wizard.StormArmor.IsBuffActive && Skills.Wizard.StormArmor.CanCast())
				return StormArmor();

			if (!Skills.Wizard.Familiar.IsBuffActive && Skills.Wizard.Familiar.CanCast())
				return Familiar();

			return null;
		}

		public TrinityPower GetDestructiblePower() => DefaultDestructiblePower();		// 可破坏物体的技能释放

		public TrinityPower GetMovementPower(Vector3 destination)
		{
			//DebugUtil.LogBuffs();

			if (Player.IsInTown)
				return Walk(destination);

			if (Player.IsGhosted)
			{
				destination = SafePotForAvoid();
				if (destination == Vector3.Zero)
					Core.Avoidance.Avoider.TryGetSafeSpot(out destination);

				return Walk(destination);
			}

			if (Core.Rift.RiftComplete && !Core.Rift.IsNephalemRift)
				return ToDestInPath(destination);

			if (Core.Avoidance.InCriticalAvoidance(Player.Position))
			{
				return ToDestInPath(destination);
			}

			TrinityPower power;
            if (TryGoOcculus(out power))
                return power;

			if (IsBlocked)
			{
				if (CanTeleport)
				{
					if (DebugMode)
						Core.Logger.Log("卡住了，传送");
					var safeSpot = Vector3.Zero;
					Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 25, 50, Player.Position);
					if (safeSpot != Vector3.Zero)
						return Teleport(safeSpot);
					Core.Avoidance.Avoider.TryGetSafeSpot(out safeSpot, 3, 25, Player.Position);
					if (safeSpot != Vector3.Zero)
						return Teleport(safeSpot);
					return Teleport(destination);
				}
				else
				{
					TrinityActor temptarget = GetClosestUnitUnSafe(20f);
					if (temptarget != null)
					{
						if (DebugMode)
							Core.Logger.Log("被包围了");
						if (Skills.Wizard.Meteor.CanCast() && Player.PrimaryResource > 60.0 && Skills.Wizard.Meteor.TimeSinceUse > 3000.0)
							return NewMeteor(temptarget);
						if (Skills.Wizard.ArcaneTorrent.CanCast() && Player.PrimaryResource > 16.0)
							return NewArcaneTorrent(temptarget);
					}
				}
			}

			return ToDestInPath(destination);

		}

	    private int __avoider_dist = 0;
		private int __avoider_num = 0;

	    private bool __avoider_generic(AvoidanceNode n)
		{
			return !Core.Avoidance.InAvoidance(n.NavigableCenter) &&
				Core.Grids.CanRayWalk(Player.Position, n.NavigableCenter) &&
				TargetUtil.NumMobsInRangeOfPosition(n.NavigableCenter, __avoider_dist) < __avoider_num;
		}

		private bool __avoider_skillonly(AvoidanceNode n)
		{
			return !Core.Avoidance.InAvoidance(n.NavigableCenter) && Core.Grids.CanRayWalk(Player.Position, n.NavigableCenter);
		}

		private Vector3 SafePotForAvoid()
		{
			Vector3 safePot = Vector3.Zero;

			Func<AvoidanceNode, bool> myf_safe = new Func<AvoidanceNode, bool>(__avoider_generic);
			Func<AvoidanceNode, bool> myf_skill = new Func<AvoidanceNode, bool>(__avoider_skillonly);

			Vector3 projectedPosition = IsBlocked
					? Core.Grids.Avoidance.GetPathCastPosition(40f, true)
					: Core.Grids.Avoidance.GetPathWalkPosition(40f, true);

			if (safePot == Vector3.Zero)
			{
				__avoider_dist = 30;
				for (int i = 1; i <= 3; i++)
				{
					__avoider_num = i;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safePot, 16f, 60f, Player.Position, myf_safe))
						break;
				}
			}

			if (safePot == Vector3.Zero)
			{
				__avoider_dist = 20;
				for (int i = 1; i <= 3; i++)
				{
					__avoider_num = i;
					if (Core.Avoidance.Avoider.TryGetSafeSpot(out safePot, 16f, 60f, Player.Position, myf_safe))
						break;
				}
			}

			if (safePot == Vector3.Zero)
			{
				safePot = projectedPosition;
			}

			if (safePot == Vector3.Zero)
			{
				Core.Logger.Warn($"没有合适的闪避点");
			}

			return safePot;
		}

		#region Settings
		public override int ClusterSize => Settings.ClusterSize;
		public override float ClusterRadius => Settings.ClusterRadius;
		public override float EmergencyHealthPct => Settings.EmergencyHealthPct;
		public float SuperEmergencyHealthPct => Settings.SuperEmergencyHealthPct;

        public override float TrashRange => 60f;
        public override float EliteRange => 60f;

		public bool DotNotAvoidWhenNephalemRift => Settings.DotNotAvoidWhenNephalemRift;
		public bool AutoChangeWeapon => Settings.AutoChangeWeapon;
		public bool AutoChangeSkills => Settings.AutoChangeSkills;

		public bool DebugMode => Settings.DebugMode;

		IDynamicSetting IRoutine.RoutineSettings => Settings;
		public TalRashasMeteorSettings Settings { get; } = new TalRashasMeteorSettings();

		public sealed class TalRashasMeteorSettings : NotifyBase, IDynamicSetting
		{
			private SkillSettings _teleport;
			private int _clusterSize;
			private int _clusterRadius;
			private float _emergencyHealthPct;
			private float _superEmergencyHealthPct;
			private bool _getStacksBeforeArchon;

            private int _castDistanceNephalemRift;
            private int _castDistanceGreaterRift;

			private bool _dotNotAvoidWhenNephalemRift;
			private bool _autoChangeWeapon;
			private bool _autoChangeSkills;

			private bool _debugMode;


			[DefaultValue(8)]
			public int ClusterSize
			{
				get { return _clusterSize; }
				set { SetField(ref _clusterSize, value); }
			}
			[DefaultValue(20)]
			public int ClusterRadius
			{
				get
				{
					return _clusterRadius;
				}
				set
				{
					SetField(ref _clusterRadius, value);
				}
			}
			[DefaultValue(0.6f)]
			public float EmergencyHealthPct
			{
				get { return _emergencyHealthPct; }
				set { SetField(ref _emergencyHealthPct, value); }
			}

			[DefaultValue(0.0f)]
			public float SuperEmergencyHealthPct
			{
				get { return _superEmergencyHealthPct; }
				set { SetField(ref _superEmergencyHealthPct, value); }
			}

            [DefaultValue(55)]
            public int CastDistanceGreaterRift
            {
                get { return _castDistanceGreaterRift; }
                set { SetField(ref _castDistanceGreaterRift, value); }
            }

            [DefaultValue(25)]
            public int CastDistanceNephalemRift
            {
                get { return _castDistanceNephalemRift; }
                set { SetField(ref _castDistanceNephalemRift, value); }
            }

			[DefaultValue(false)]
            public bool AutoChangeWeapon
            {
                get { return _autoChangeWeapon; }
                set { SetField(ref _autoChangeWeapon, value); }
            }

			[DefaultValue(false)]
            public bool DotNotAvoidWhenNephalemRift
            {
                get { return _dotNotAvoidWhenNephalemRift; }
                set { SetField(ref _dotNotAvoidWhenNephalemRift, value); }
            }

			[DefaultValue(false)]
            public bool AutoChangeSkills
            {
                get { return _autoChangeSkills; }
                set { SetField(ref _autoChangeSkills, value); }
            }

			[DefaultValue(false)]
            public bool DebugMode
            {
                get { return _debugMode; }
                set { SetField(ref _debugMode, value); }
            }

			public SkillSettings Teleport
			{
				get { return _teleport; }
				set { SetField(ref _teleport, value); }
			}
			#region Skill Defaults

			private static readonly SkillSettings TeleportDefaults = new SkillSettings
			{
				UseMode = UseTime.Default,
				RecastDelayMs = 200,
				Reasons = UseReasons.Blocked
			};

			#endregion

			public override void LoadDefaults()
			{
				base.LoadDefaults();
				Teleport = TeleportDefaults.Clone();
			}

			#region IDynamicSetting

			public string GetName() => GetType().Name;
			public UserControl GetControl() => UILoader.LoadXamlByFileName<UserControl>(GetName() + "_en.xaml");
			public object GetDataContext() => this;
			public string GetCode() => JsonSerializer.Serialize(this);
			public void ApplyCode(string code) => JsonSerializer.Deserialize(code, this, true);
			public void Reset() => LoadDefaults();
			public void Save() { }

			#endregion
		}

		#endregion
	}
}