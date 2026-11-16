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

        public void Apply(BackupStore backup, Action<string> log)
        {
            log("▸ " + Title);
            foreach (var a in Actions)
            {
                try { a.Apply(backup, log); }
                catch (Exception ex) { log("  ошибка: " + ex.Message); }
            }
        }

        public void Revert(BackupStore backup, Action<string> log)
        {
            log("◂ " + Title);
            foreach (var a in Actions.Reverse())
            {
                try { a.Revert(backup, log); }
                catch (Exception ex) { log("  ошибка: " + ex.Message); }
            }
        }
    }
}
