using System;
using System.Collections.Generic;

namespace Tactics.Core
{
    // Portable, versioned catalogue. Publish together with the matching rules assembly.
    [Serializable]
    public sealed class MatchContentData
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion;
        public string Version;
        public List<MatchUnitTypeData> Units = new List<MatchUnitTypeData>();
        public List<MatchRaceData> Races = new List<MatchRaceData>();
        public List<MatchDamageData> Damage = new List<MatchDamageData>();

        public static MatchContentData FromContent(MatchContent content, IDictionary<string, string> leadersByRace, string version = "1")
        {
            if (content == null || leadersByRace == null) throw new ArgumentNullException();
            content.Validate();
            var data = new MatchContentData { SchemaVersion = CurrentVersion, Version = version };
            foreach (var unit in content.UnitTypes) data.Units.Add(new MatchUnitTypeData { Id = unit.Id, Health = unit.Health,
                Attack = unit.Attack, Movement = unit.Movement, Range = unit.Range, Cost = unit.Cost });
            data.Units.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var raceId in content.RaceIds)
            {
                if (!leadersByRace.TryGetValue(raceId, out var leader)) throw new ArgumentException("Missing race leader: " + raceId);
                var race = new MatchRaceData { Id = raceId, LeaderTypeId = leader };
                race.RecruitTypeIds.AddRange(content.Recruits(raceId));
                race.RecruitTypeIds.Sort(StringComparer.Ordinal);
                data.Races.Add(race);
            }
            if (data.Races.Count != leadersByRace.Count) throw new ArgumentException("Leader catalogue contains unknown races.");
            data.Races.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var attacker in data.Units) foreach (var defender in data.Units)
                data.Damage.Add(new MatchDamageData { AttackerTypeId = attacker.Id, DefenderTypeId = defender.Id,
                    Percent = content.DamagePercent(attacker.Id, defender.Id) });
            data.ToContent();
            return data;
        }

        public MatchContent ToContent()
        {
            if (SchemaVersion != CurrentVersion || !MatchSnapshotData.ValidId(Version)
                || Units == null || Units.Count < 1 || Units.Count > 256 || Races == null || Races.Count < 1 || Races.Count > 64
                || Damage == null || Damage.Count != Units.Count * Units.Count) throw new ArgumentException("Invalid content catalogue header.");
            var content = new MatchContent();
            var unitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in Units)
            {
                if (unit == null || !MatchSnapshotData.ValidId(unit.Id) || !unitIds.Add(unit.Id)) throw new ArgumentException("Invalid or repeated unit type.");
                content.AddUnit(new UnitTypeStats(unit.Id, unit.Health, unit.Attack, unit.Movement, unit.Range, unit.Cost));
            }
            var raceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var race in Races)
            {
                if (race == null || !MatchSnapshotData.ValidId(race.Id) || !raceIds.Add(race.Id)
                    || race.LeaderTypeId == null || !unitIds.Contains(race.LeaderTypeId) || race.RecruitTypeIds == null
                    || race.RecruitTypeIds.Count > Units.Count) throw new ArgumentException("Invalid race or leader.");
                var recruits = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in race.RecruitTypeIds)
                    if (id == null || !unitIds.Contains(id) || !recruits.Add(id)) throw new ArgumentException("Invalid or repeated recruit type.");
                content.AddRace(race.Id, recruits);
            }
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Damage)
            {
                if (entry == null || entry.AttackerTypeId == null || entry.DefenderTypeId == null
                    || !unitIds.Contains(entry.AttackerTypeId) || !unitIds.Contains(entry.DefenderTypeId)
                    || !pairs.Add(entry.AttackerTypeId + "\n" + entry.DefenderTypeId)) throw new ArgumentException("Invalid or repeated damage matchup.");
                content.SetDamage(entry.AttackerTypeId, entry.DefenderTypeId, entry.Percent);
            }
            content.Validate();
            return content;
        }
    }

    [Serializable]
    public sealed class MatchUnitTypeData
    {
        public string Id;
        public float Health;
        public int Attack, Movement, Range, Cost;
    }
    [Serializable]
    public sealed class MatchRaceData
    {
        public string Id, LeaderTypeId;
        public List<string> RecruitTypeIds = new List<string>();
    }
    [Serializable]
    public sealed class MatchDamageData
    {
        public string AttackerTypeId, DefenderTypeId;
        public float Percent;
    }
}
