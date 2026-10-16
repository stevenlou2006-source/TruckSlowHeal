using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TruckSlowHeal
{
	[BepInPlugin("cn_xc.TruckSlowHeal", "Truck Slow Heal", "0.1.0")]
	public class TruckSlowHealPlugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;

		// 固定回血速率：每秒回最大血量的 5%
		private const float HEAL_PERCENT_PER_SECOND = 0.05f;

		internal static ConfigEntry<bool> EnableLog;

		private void Awake()
		{
			Log = Logger;

			EnableLog = Config.Bind("General", "EnableLog", false,
				"输出详细日志（状态和回血记录）");

			var go = new GameObject("TruckSlowHealBehaviour");
			go.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(go);
			go.AddComponent<TruckHealBehaviour>();
			Log.LogInfo($"[TruckHeal] 卡车内自动缓慢回血已加载（每秒回 {HEAL_PERCENT_PER_SECOND * 100f:F0}% 上限血量，日志={(EnableLog.Value ? "开" : "关")}）");
		}

		internal static float GetHealPerSecond(int maxHp)
		{
			return maxHp * HEAL_PERCENT_PER_SECOND;
		}
	}

	public class TruckHealBehaviour : MonoBehaviour
	{
		private float _healAccumulator = 0f;
		private float _lastLogTime = 0f;
		private const float LOG_INTERVAL = 2f;

		private AccessTools.FieldRef<PlayerHealth, int> _healthRef;
		private AccessTools.FieldRef<PlayerHealth, int> _maxHealthRef;
		private AccessTools.FieldRef<RoomVolumeCheck, bool> _inTruckRef;

		private void EnsureRefs()
		{
			if (_healthRef != null) return;
			_healthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("health");
			_maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");
			_inTruckRef = AccessTools.FieldRefAccess<RoomVolumeCheck, bool>("inTruck");
		}

		private void Update()
		{
			try
			{
				EnsureRefs();

				PlayerAvatar avatar = SemiFunc.PlayerGetLocal();
				if (avatar == null)
				{
					LogStatus("本地玩家为 null");
					return;
				}

				PlayerHealth health = avatar.GetComponent<PlayerHealth>();
				if (health == null)
				{
					LogStatus("本地 PlayerHealth 为 null");
					return;
				}

				int hp = _healthRef(health);
				int maxHp = _maxHealthRef(health);

				bool inTruck = false;
				int checkCount = 0;
				var checks = avatar.GetComponentsInChildren<RoomVolumeCheck>(true);
				if (checks != null)
				{
					checkCount = checks.Length;
					foreach (var c in checks)
					{
						if (c == null) continue;
						if (_inTruckRef(c)) { inTruck = true; break; }
					}
				}

				float healPerSecond = TruckSlowHealPlugin.GetHealPerSecond(maxHp);

				LogStatus($"RoomCheck={checkCount} inTruck={inTruck} HP={hp}/{maxHp} 回血={healPerSecond:F2}/s 累积={_healAccumulator:F2}");

				if (!inTruck)
				{
					_healAccumulator = 0f;
					return;
				}

				if (hp >= maxHp)
				{
					_healAccumulator = 0f;
					return;
				}

				_healAccumulator += Time.deltaTime * healPerSecond;

				if (_healAccumulator >= 1f)
				{
					int toHeal = Mathf.Min((int)_healAccumulator, maxHp - hp);
					if (toHeal > 0)
					{
						_healAccumulator -= toHeal;
						health.Heal(toHeal, false);

						if (TruckSlowHealPlugin.EnableLog.Value)
							TruckSlowHealPlugin.Log.LogInfo($"[TruckHeal] 回血 +{toHeal}，当前 HP={hp + toHeal}/{maxHp}（速率 {healPerSecond:F2}/s）");
					}
				}
			}
			catch (Exception ex)
			{
				TruckSlowHealPlugin.Log.LogError($"[TruckHeal] Behaviour 异常: {ex}");
			}
		}

		private void LogStatus(string msg)
		{
			if (!TruckSlowHealPlugin.EnableLog.Value) return;
			if (Time.time - _lastLogTime < LOG_INTERVAL) return;
			_lastLogTime = Time.time;
			TruckSlowHealPlugin.Log.LogInfo($"[TruckHeal-Behaviour] {msg}");
		}
	}
}