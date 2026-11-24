using System;
using System.Collections.Generic;
using System.Linq;

namespace HelloCleanUp.Core
{
    public enum TweakState { NotApplied, Partial, Applied }

    public sealed class Tweak
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public bool Recommended { get; private set; }
        public IList<ITweakAction> Actions { get; private set; }

        public Tweak(string id, string title, string description, bool recommended, params ITweakAction[] actions)
        {
            Id = id; Title = title; Description = description; Recommended = recommended;
            Actions = actions;
        }

        public TweakState GetState()
        {
            int applied = 0;
            foreach (var a in Actions)
            {
                bool ok;
                try { ok = a.IsApplied(); } catch { ok = false; }
                if (ok) applied++;
            }
            if (applied == 0) return TweakState.NotApplied;
            return applied == Actions.Count ? TweakState.Applied : TweakState.Partial;
        }

        /// <summary>One line per change Apply would make; empty when everything is already in place.</summary>
        public IList<string> PlannedChanges()
        {
            var list = new List<string>();
            foreach (var a in Actions)
            {
                string d;
                try { d = a.Describe(); }
                catch (Exception ex) { d = a + ": не удалось прочитать текущее значение (" + ex.Message + ")"; }
                if (d != null) list.Add(d);
            }
            return list;
        }

        /// <summary>Returns the number of actions that failed.</summary>
        public int Apply(BackupStore backup, Action<string> log)
        {
            log("▸ " + Title);
            int errors = 0;
            foreach (var a in Actions)
            {
                try { a.Apply(backup, log); }
                catch (Exception ex) { errors++; log("  ошибка: " + ex.Message); }
            }
            return errors;
        }

        /// <summary>Returns the number of actions that failed.</summary>
        public int Revert(BackupStore backup, Action<string> log)
        {
            log("◂ " + Title);
            int errors = 0;
            foreach (var a in Actions.Reverse())
            {
                try { a.Revert(backup, log); }
                catch (Exception ex) { errors++; log("  ошибка: " + ex.Message); }
            }
            return errors;
        }
    }
}
