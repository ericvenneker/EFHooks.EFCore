using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

namespace EFHooks
{
    public partial class HookedDbContext
    {
        private class HookRunner
        {
            private readonly HookedDbContext _ctx;
            private readonly HookedEntityEntry[] _modifiedEntries;

            public HookRunner(HookedDbContext ctx)
            {
                _ctx = ctx;
                _modifiedEntries =
                    ctx.ChangeTracker.Entries()
                        .Where(x => x.State != EntityState.Unchanged && x.State != EntityState.Detached)
                        .Select(x => new HookedEntityEntry() {
                            Entity = x.Entity,
                            PreSaveState = x.State
                        })
                        .ToArray();

            }

            public void RunPreActionHooks()
            {
                ExecutePreActionHooks(_modifiedEntries, false);//Regardless of validation (executing the hook possibly fixes validation errors)

                if (!_ctx.ValidateOnSaveEnabled || _ctx.Validate())
                {
                    ExecutePreActionHooks(_modifiedEntries, true);
                }
            }


            /// <summary>
            /// Executes the pre action hooks, filtered by <paramref name="requiresValidation"/>.
            /// </summary>
            /// <param name="modifiedEntries">The modified entries to execute hooks for.</param>
            /// <param name="requiresValidation">if set to <c>true</c> executes hooks that require validation, otherwise executes hooks that do NOT require validation.</param>
            private void ExecutePreActionHooks(IEnumerable<HookedEntityEntry> modifiedEntries, bool requiresValidation)
            {
                foreach (var entityEntry in modifiedEntries)
                {
                    var entry = entityEntry; //Prevents access to modified closure

                    foreach (var hook in _ctx.PreHooks.Where(x => (x.HookStates & entry.PreSaveState) == entry.PreSaveState && x.RequiresValidation == requiresValidation))
                    {
                        var metadata = new HookEntityMetadata(entityEntry.PreSaveState, _ctx);
                        hook.HookObject(entityEntry.Entity, metadata);

                        if (metadata.HasStateChanged)
                        {
                            entityEntry.PreSaveState = metadata.State;
                        }
                    }
                }
            }

            public void RunPostActionHooks()
            {
                var hasPostHooks = _ctx.PostHooks.Any(); // Save this to a local variable since we're checking this again later.
                if (hasPostHooks)
                {
                    foreach (var entityEntry in _modifiedEntries)
                    {
                        var entry = entityEntry;

                        //Obtains hooks that 'listen' to one or more Entity States
                        foreach (var hook in _ctx.PostHooks.Where(x => (x.HookStates & entry.PreSaveState) == entry.PreSaveState))
                        {
                            var metadata = new HookEntityMetadata(entityEntry.PreSaveState, _ctx);
                            hook.HookObject(entityEntry.Entity, metadata);
                        }
                    }
                }
            }
        }
    }
}