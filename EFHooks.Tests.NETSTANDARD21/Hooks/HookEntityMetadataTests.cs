using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

namespace EFHooks.Tests.Hooks
{
    public class HookEntityMetadataTests
    {
        private class LocalContext : HookedDbContext
        {
            public LocalContext(DbContextOptions<LocalContext> contextOptions)
                : base(contextOptions)
            {

            }
        }

        [Test]
        public void HookEntityMetadata_HasEntityState()
        {
            var result = new HookEntityMetadata(EntityState.Deleted) {
                State = EntityState.Modified
            };
            Assert.AreEqual(EntityState.Modified, result.State);
        }

        [Test]
        public void HookEntityMetadata_OnlyShowsEntityStateChangeAfterModification()
        {
            var result = new HookEntityMetadata(EntityState.Deleted);
            Assert.AreEqual(false, result.HasStateChanged);
            result.State = EntityState.Modified;
            Assert.AreEqual(true, result.HasStateChanged);
        }

        [Test]
        public void HookEntityMetadata_EntityStateChangedIsFalse_AfterReassigningSameValue()
        {
            var result = new HookEntityMetadata(EntityState.Modified);
            Assert.AreEqual(false, result.HasStateChanged);
            result.State = EntityState.Modified;
            Assert.AreEqual(false, result.HasStateChanged);
        }

        [Test]
        public void HookEntityMetadata_MetadataWithContext()
        {
            var contextBuilder =
                new DbContextOptionsBuilder<LocalContext>()
                    //.UseInMemoryDatabase("HookEntityMetadata_MetadataWithContext")
                    .UseSqlite("DataSource=:memory:");
            var context = new LocalContext(contextBuilder.Options);
            var result = new HookEntityMetadata(EntityState.Modified, context);
            Assert.AreEqual(context, result.CurrentContext);
        }
    }
}