namespace SylvaNote.Core.FirstRun
{
    // Baked, not generated: a second device seeding before sync is configured has to
    // produce the same item ids, or the subtree duplicates on the server instead of
    // merging under LWW (decisions.md). They are UUIDv7-shaped with a fixed timestamp
    // prefix and a readable counter, so the note:// links in the seed markdown can be
    // matched up by eye.
    public static class SeedIds
    {
        public const string GettingStartedNote = "019b76da-a800-7000-8000-000000000001";
        public const string EditorNote = "019b76da-a800-7000-8000-000000000002";
        public const string BoardsNote = "019b76da-a800-7000-8000-000000000003";
        public const string TemplatesNote = "019b76da-a800-7000-8000-000000000004";
        public const string SearchNote = "019b76da-a800-7000-8000-000000000005";
        public const string SyncNote = "019b76da-a800-7000-8000-000000000006";
        public const string AgentsNote = "019b76da-a800-7000-8000-000000000007";

        public const string IconAttachment = "019b76da-a800-7000-8000-0000000000a1";

        public const string LearnBoard = "019b76da-a800-7000-8000-000000000101";
        public const string ToTryColumn = "019b76da-a800-7000-8000-000000000111";
        public const string DoneColumn = "019b76da-a800-7000-8000-000000000112";

        public const string FirstNoteTask = "019b76da-a800-7000-8000-000000000121";
        public const string LinkNotesTask = "019b76da-a800-7000-8000-000000000122";
        public const string AttachImageTask = "019b76da-a800-7000-8000-000000000123";
        public const string MakeTemplateTask = "019b76da-a800-7000-8000-000000000124";
        public const string SetUpSyncTask = "019b76da-a800-7000-8000-000000000125";
        public const string ConnectAgentTask = "019b76da-a800-7000-8000-000000000126";

        public const string ProjectTemplate = "019b76da-a800-7000-8000-000000000201";
        public const string ProjectOverview = "019b76da-a800-7000-8000-000000000202";
        public const string ProjectIdeas = "019b76da-a800-7000-8000-000000000203";
        public const string ProjectLog = "019b76da-a800-7000-8000-000000000204";
    }
}
