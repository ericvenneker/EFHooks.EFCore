using System;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

using EFHooks.Tests.Hooks;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using NUnit.Framework;

namespace EFHooks.Tests
{
    public partial class HookedDbContextTests
    {
        public class LocalContextFactory : IDisposable
        {
            private DbConnection _connection;

            private DbContextOptions<THookedDbContext> CreateOptions<THookedDbContext>()
                where THookedDbContext : HookedDbContext
            {
                return
                    new DbContextOptionsBuilder<THookedDbContext>()
                        .UseSqlite(_connection).Options;
            }

            public THookedDbContext CreateContext<THookedDbContext>(Func<DbContextOptions<THookedDbContext>, THookedDbContext> constructor)
                where THookedDbContext: HookedDbContext
            {
                if (_connection == null)
                {
                    _connection = new SqliteConnection("DataSource=:memory:");
                    _connection.Open();

                    var options = CreateOptions<THookedDbContext>();
                    using (var context = constructor(options))
                    {
                        context.Database.EnsureCreated();
                    }
                }

                return constructor(CreateOptions<THookedDbContext>());
            }

            public void Dispose()
            {
                if (_connection != null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }
        }

        private class TimestampPostLoadHook : PostLoadHook<ITimeStamped>
        {
            public bool HasRun { get; private set; }

            public override void Hook(ITimeStamped entity, HookEntityMetadata metadata)
            {
                HasRun = true;
            }
        }

        private class TimestampPreInsertHook : PreInsertHook<ITimeStamped>
        {
            public override bool RequiresValidation
            {
                get { return true; }
            }
            public override void Hook(ITimeStamped entity, HookEntityMetadata metadata)
            {
                entity.CreatedAt = DateTime.Now;
            }
        }

        private class TimestampPreUpdateHook : PreUpdateHook<ITimeStamped>
        {
            public override bool RequiresValidation
            {
                get { return false; }
            }
            public override void Hook(ITimeStamped entity, HookEntityMetadata metadata)
            {
                entity.ModifiedAt = DateTime.Now;
            }
        }

        private class TimestampPostInsertHook : PostInsertHook<ITimeStamped>
        {
            public override void Hook(ITimeStamped entity, HookEntityMetadata metadata)
            {
                entity.ModifiedAt = DateTime.Now;
            }
        }

        private class RunCheckedPreInsertHook : PreInsertHook<object>
        {
            public override bool RequiresValidation
            {
                get { return true; }
            }
            public bool HasRun { get; private set; }
            public override void Hook(object entity, HookEntityMetadata metadata)
            {
                HasRun = true;
            }
        }

        private class LocalContext : HookedDbContext
        {
            public LocalContext(DbContextOptions<LocalContext> contextOptions)
                : base(contextOptions)
            {
                Database.EnsureCreated();
            }

            public LocalContext(DbContextOptions<LocalContext> contextOptions, IHook[] hooks)
                : base(contextOptions, hooks)
            {
                Database.EnsureCreated();
            }

            public DbSet<TimestampedSoftDeletedEntity> Entities { get; set; }
            public DbSet<ValidatedEntity> ValidatedEntities { get; set; }
        }

        [Test]
        public void HookedDbContext_ConstructsWithHooks()
        {
            using (var factory = new LocalContextFactory())
            {
                var hooks = new IHook[]
                                {
                                    new TimestampPreInsertHook()
                                };

                _ = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));
            }
        }

        [Test]
        public void HookedDbContext_MustNotCallHooks_WhenGetValidationErrorsIsCalled()
        {
            using (var factory = new LocalContextFactory())
            {
                var hooks = new IHook[]
                            {
                                    new TimestampPreInsertHook()
                            };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));
                var entity = new TimestampedSoftDeletedEntity();
                context.Entities.Add(entity);

                Assert.AreNotEqual(entity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_MustCallHooks_WhenRunningSaveChanges()
        {
            using (var factory = new LocalContextFactory())
            {
                var hooks = new IHook[]
                            {
                                    new TimestampPreInsertHook()
                            };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));
                var entity = new TimestampedSoftDeletedEntity();
                context.Entities.Add(entity);
                context.SaveChanges();

                Assert.AreEqual(entity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_MustCallHooks_WhenMaterializingObject()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                var hook = new TimestampPostLoadHook();
                var entity = new TimestampedSoftDeletedEntity() { CreatedAt = DateTime.Now };
                context.Entities.Add(entity);
                context.SaveChanges();
                int id = entity.Id;

                context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(hook);
                _ = context.Entities.Find(id);

                Assert.IsTrue(hook.HasRun);
            }
        }

        [Test]
        public void HookedDbContext_MustNotCallHooks_IfModelIsInvalid()
        {
            using (var factory = new LocalContextFactory())
            {
                var hooks = new IHook[]
                            {
                                    new TimestampPreInsertHook()
                            };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));
                var validatedEntity = new ValidatedEntity();
                context.ValidateOnSaveEnabled = true;
                context.ValidatedEntities.Add(validatedEntity);

                Assert.Throws<ValidationException>(() => context.SaveChanges());

                Assert.AreNotEqual(validatedEntity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_MustCallHooks_IfModelIsInvalidButUnchanged()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(new TimestampPreInsertHook());
                var tsEntity = new TimestampedSoftDeletedEntity();
                var valEntity = new ValidatedEntity();

                context.Entities.Add(tsEntity);
                context.Entry(valEntity).State = EntityState.Unchanged;

                Assert.DoesNotThrow(() => context.SaveChanges());

                Assert.AreEqual(tsEntity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_AfterConstruction_CanRegisterNewHooks()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(new TimestampPreInsertHook());

                var entity = new TimestampedSoftDeletedEntity();
                context.Entities.Add(entity);
                context.SaveChanges();

                Assert.AreEqual(entity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_ShouldNotHook_IfAnyChangedObjectsAreInvalid()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(new TimestampPreInsertHook());
                var tsEntity = new TimestampedSoftDeletedEntity();
                var valEntity = new ValidatedEntity();

                context.ValidateOnSaveEnabled = true;
                context.Entities.Add(tsEntity);
                context.ValidatedEntities.Add(valEntity);

                Assert.Throws<ValidationException>(() => context.SaveChanges());

                Assert.AreNotEqual(tsEntity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_ShouldHook_IfValidateBeforeSaveIsDisabled_AndChangedObjectsAreInvalid()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options =>
                new LocalContext(options) {
                    ValidateOnSaveEnabled = false
                });
                context.RegisterHook(new TimestampPreInsertHook());
                var tsEntity = new TimestampedSoftDeletedEntity();
                var valEntity = new ValidatedEntity();

                context.Entities.Add(tsEntity);
                context.ValidatedEntities.Add(valEntity);

                Assert.IsTrue(context.GetValidationErrors().Any());

                Assert.Throws<DbUpdateException>(() => context.SaveChanges());

                Assert.AreEqual(tsEntity.CreatedAt.Date, DateTime.Today);
                Assert.AreEqual(valEntity.CreatedAt.Date, DateTime.Today);
            }
        }

        [Test]
        public void HookedDbContext_ShouldPostHook_IfNoExceptionIsHit()
        {
            using (var factory = new LocalContextFactory())
            {
                var runCheckingHook = new RunCheckedPreInsertHook();
                var hooks = new IHook[]
                                {
                                    runCheckingHook,
                                    new TimestampPostInsertHook()
                                };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));

                var tsEntity = new TimestampedSoftDeletedEntity {
                    CreatedAt = DateTime.Now
                };
                context.Entities.Add(tsEntity);
                context.SaveChanges();

                Assert.IsTrue(runCheckingHook.HasRun);
                Assert.AreEqual(DateTime.Today, tsEntity.ModifiedAt.Value.Date);
            }
        }

        [Test]
        public void HookedDbContext_ShouldNotPostHook_IfExceptionIsHit()
        {
            using (var factory = new LocalContextFactory())
            {
                var runCheckingHook = new RunCheckedPreInsertHook();
                var hooks = new IHook[]
                                {
                                    runCheckingHook,
                                    new TimestampPostInsertHook()
                                };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));

                var valEntity = new ValidatedEntity {
                    CreatedAt = DateTime.Now
                };
                context.ValidateOnSaveEnabled = true;
                context.ValidatedEntities.Add(valEntity);

                Assert.Throws<ValidationException>(() => context.SaveChanges());

                Assert.IsFalse(runCheckingHook.HasRun);
                Assert.IsFalse(valEntity.ModifiedAt.HasValue);
            }
        }

        [Test]
        public void HookedDbContext_CanLateBindPostActionHooks()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(new TimestampPostInsertHook());

                var tsEntity = new TimestampedSoftDeletedEntity {
                    CreatedAt = DateTime.Now
                };
                context.Entities.Add(tsEntity);
                context.SaveChanges();

                Assert.AreEqual(DateTime.Today, tsEntity.ModifiedAt.Value.Date);
            }
        }

        [Test]
        public void HookedDbContext_MustOnlyHookWhenObjectIsInTheSameState()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                context.RegisterHook(new TimestampPreInsertHook());
                context.RegisterHook(new TimestampPreUpdateHook());

                var tsEntity = new TimestampedSoftDeletedEntity {
                    CreatedAt = DateTime.Now
                };
                context.Entities.Add(tsEntity);
                context.SaveChanges();

                Assert.AreEqual(DateTime.Today, tsEntity.CreatedAt.Date);
                Assert.IsFalse(tsEntity.ModifiedAt.HasValue);
            }
        }

        [Test]
        public void HookedDbContext_PreActionHookMethod_MustHaveTheContextPassedInTheMetadata()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                var preAction = A.Fake<PreInsertHook<ITimeStamped>>();
                A.CallTo(() => preAction.HookStates).Returns(EntityState.Added);

                context.RegisterHook(preAction);

                // We aren't testing the hook here so just set the createdat date so that SaveChanges passes
                var entity = new TimestampedSoftDeletedEntity { CreatedAt = DateTime.Now };
                context.Entities.Add(entity);
                context.SaveChanges();
                A.CallTo(() => preAction.Hook(entity, A<HookEntityMetadata>.That.Matches(m => m.CurrentContext == context))).MustHaveHappened();
            }
        }

        [Test]
        public void HookedDbContext_PostActionHookMethod_MustHaveTheContextPassedInTheMetadata()
        {
            using (var factory = new LocalContextFactory())
            {
                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options));
                var postAction = A.Fake<PostInsertHook<ITimeStamped>>();
                A.CallTo(() => postAction.HookStates).Returns(EntityState.Added);

                context.RegisterHook(postAction);

                // We aren't testing the hook here
                var entity = new TimestampedSoftDeletedEntity { CreatedAt = DateTime.Now };
                context.Entities.Add(entity);
                context.SaveChanges();
                A.CallTo(() => postAction.Hook(entity, A<HookEntityMetadata>.That.Matches(m => m.CurrentContext == context))).MustHaveHappened();
            }
        }

        [Test]
        public async Task HookedDbContext_MustCallHooks_WhenRunningSaveChangesAsync()
        {
            using (var factory = new LocalContextFactory())
            {
                var hooks = new IHook[]
                                {
                                new TimestampPreInsertHook()
                                };

                var context = factory.CreateContext<LocalContext>(options => new LocalContext(options, hooks));
                var entity = new TimestampedSoftDeletedEntity();
                context.Entities.Add(entity);
                await context.SaveChangesAsync();

                Assert.AreEqual(entity.CreatedAt.Date, DateTime.Today);
            }
        }
    }
}