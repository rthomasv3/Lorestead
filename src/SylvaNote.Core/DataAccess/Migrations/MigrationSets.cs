using System.Collections.Generic;

namespace SylvaNote.Core.DataAccess.Migrations
{
    // Each host composes its migrator from these: the server DB runs Shared only, the
    // client DB runs Shared + Client. Version numbers are global across both lists.
    public static class MigrationSets
    {
        public static IReadOnlyList<IMigration> Shared()
        {
            return new IMigration[]
            {
                new Db001_CoreSchema(),
            };
        }

        public static IReadOnlyList<IMigration> Client()
        {
            return new IMigration[]
            {
                new Db001_CoreSchema(),
                new Db002_ClientState(),
                new Db003_AttachmentThumbnail(),
            };
        }
    }
}
