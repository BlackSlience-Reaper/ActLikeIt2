using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace ActLikeIt2.Runtime;

internal static class ActBossOrderUtil
{
	public static bool HasEncounteredBossInRun(IRunState runState, string bossEntry)
	{
		foreach (IReadOnlyList<MapPointHistoryEntry> actHistory in runState.MapPointHistory)
		{
			foreach (MapPointHistoryEntry point in actHistory)
			{
				foreach (MapPointRoomHistoryEntry room in point.Rooms)
				{
					if (room.RoomType == RoomType.Boss
						&& string.Equals(room.ModelId?.Entry, bossEntry, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}

		return false;
	}

	public static void ApplyForcedBossOrder(ActModel act, IRunState runState, ActRegistration registration)
	{
		if (registration.ForcedBossOrder.Length == 0)
		{
			return;
		}

		foreach (string bossEntry in registration.ForcedBossOrder)
		{
			if (HasEncounteredBossInRun(runState, bossEntry))
			{
				continue;
			}

			EncounterModel? boss = act.AllBossEncounters
				.FirstOrDefault(e => string.Equals(e.Id.Entry, bossEntry, StringComparison.OrdinalIgnoreCase));
			if (boss == null)
			{
				boss = ModelDb.AllEncounters
					.FirstOrDefault(e => e.RoomType == RoomType.Boss
						&& string.Equals(e.Id.Entry, bossEntry, StringComparison.OrdinalIgnoreCase));
			}

			if (boss != null)
			{
				act.SetBossEncounter(boss);
				return;
			}

			ActLikeIt2Mod.Log.Warn($"Forced boss '{bossEntry}' for act '{act.Id.Entry}' was not found.");
		}
	}
}
