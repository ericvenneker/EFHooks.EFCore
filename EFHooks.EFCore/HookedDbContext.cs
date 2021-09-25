using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EFHooks
{
	/// <summary>
	/// An Entity Framework DbContext that can be hooked into by registering EFHooks.IHook objects.
	/// </summary>
	public partial class HookedDbContext : DbContext
    {
        /// <summary>
        /// The pre-action hooks.
        /// </summary>
        protected IList<IPreActionHook> PreHooks { get; private set; }
        /// <summary>
        /// The post-action hooks.
        /// </summary>
        protected IList<IPostActionHook> PostHooks { get; private set; }

        /// <summary>
        /// The Post load hooks.
        /// </summary>
        protected IList<IPostLoadHook> PostLoadHooks { get; private set; }
        public bool ValidateOnSaveEnabled { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HookedDbContext" /> class, initializing empty lists of hooks.
        /// </summary>
        public HookedDbContext()
            : base()
        {
            InitializeHooks();
            ListenToObjectMaterialized();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HookedDbContext" /> class, filling <see cref="PreHooks"/> and <see cref="PostHooks"/>.
        /// </summary>
        /// <param name="hooks">The hooks.</param>
        public HookedDbContext(IHook[] hooks)
            : base()
        {
            AttachHooks(hooks);
            ListenToObjectMaterialized();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HookedDbContext" /> class using the an existing connection to connect 
        /// to a database. The connection will not be disposed when the context is disposed. (see <see cref="DbContext"/> overloaded constructor)
        /// </summary>
        /// <param name="options">Context options to use for the new context.</param>
        public HookedDbContext(DbContextOptions options)
            : base(options)
        {
            InitializeHooks();
            ListenToObjectMaterialized();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HookedDbContext" /> class using the an existing connection to connect 
        /// to a database. The connection will not be disposed when the context is disposed. (see <see cref="DbContext"/> overloaded constructor)
        /// </summary>
        /// <param name="options">Context options to use for the new context.</param>
        /// <param name="hooks">The hooks.</param>
        public HookedDbContext(DbContextOptions options, IHook[] hooks)
            : base(options)
        {
            AttachHooks(hooks);
            ListenToObjectMaterialized();
        }

        private void InitializeHooks()
        {
            PreHooks = new List<IPreActionHook>();
            PostHooks = new List<IPostActionHook>();
            PostLoadHooks = new List<IPostLoadHook>();
        }

        private void AttachHooks(IHook[] hooks)
        {
            if (hooks == null) {
                InitializeHooks();
                return;
            };

            PreHooks = hooks.OfType<IPreActionHook>().ToList();
            PostHooks = hooks.OfType<IPostActionHook>().ToList();
            PostLoadHooks = hooks.OfType<IPostLoadHook>().ToList();
        }

        /// <summary>
        /// Registers a hook to run before a database action occurs.
        /// </summary>
        /// <param name="hook">The hook to register.</param>
        public void RegisterHook(IPreActionHook hook)
        {
            PreHooks.Add(hook);
        }

        /// <summary>
        /// Registers a hook to run after a database action occurs.
        /// </summary>
        /// <param name="hook">The hook to register.</param>
        public void RegisterHook(IPostActionHook hook)
        {
            PostHooks.Add(hook);
        }

        /// <summary>
        /// Registers a hook to run after a database load occurs.
        /// </summary>
        /// <param name="hook">The hook to register.</param>
        public void RegisterHook(IPostLoadHook hook)
        {
            PostLoadHooks.Add(hook);
        }

        /// <summary>
        /// Saves all changes made in this context to the underlying database.
        /// </summary>
        /// <returns>
        /// The number of objects written to the underlying database.
        /// </returns>
        public override int SaveChanges()
        {
            var hookExecution = new HookRunner(this);
            int result;

            hookExecution.RunPreActionHooks();

            result = base.SaveChanges();

            hookExecution.RunPostActionHooks();

            return result;
        }

        /// <summary>
        /// Asynchronously saves all changes made in this context to the underlying database.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="T:System.Threading.CancellationToken" /> to observe while waiting for the task to complete.</param>
        /// <returns>
        /// A task that represents the asynchronous save operation.
        /// The task result contains the number of objects written to the underlying database.
        /// </returns>
        /// <remarks>
        /// Multiple active operations on the same context instance are not supported.  Use 'await' to ensure
        /// that any asynchronous operations have completed before calling another method on this context.
        /// </remarks>
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var hookExecution = new HookRunner(this);
            hookExecution.RunPreActionHooks();
            var result = await base.SaveChangesAsync(cancellationToken);
            hookExecution.RunPostActionHooks();
            return result;
        }

        protected bool Validate()
        {
            bool isvalid = true;
            List<ValidationResult> validationResults = GetValidationErrors(true);

            if (validationResults.Any())
            {
                isvalid = false;
            }

            return isvalid;
        }

        public List<ValidationResult> GetValidationErrors()
        {
            return GetValidationErrors(false);
        }

        private List<ValidationResult> GetValidationErrors(bool throwValidationException)
        {
            var validationResults = new List<ValidationResult>();
            IEnumerable<object> entities;

            entities =
                ChangeTracker
                    .Entries()
                    .Where(entry => entry.State == EntityState.Modified || entry.State == EntityState.Added)
                    .Select(entry => entry.Entity);

            foreach (var entity in entities)
            {
                if (!Validator.TryValidateObject(entity, new ValidationContext(entity), validationResults, true))
                {
                    if (throwValidationException)
                    {
                        throw new ValidationException();
                    }
                }
            }

            return validationResults;
        }

        private void ListenToObjectMaterialized()
        {
            ChangeTracker.Tracked += ChangeTracker_Tracked;
        }

        private void ChangeTracker_Tracked(object sender, EntityTrackedEventArgs e)
        {
            if (e.Entry.State == EntityState.Unchanged)
            {
                var metadata = new HookEntityMetadata(EntityState.Unchanged, this);

                foreach (var postLoadHook in PostLoadHooks)
                {
                    postLoadHook.HookObject(e.Entry.Entity, metadata);
                }
            }
        }
    }
}