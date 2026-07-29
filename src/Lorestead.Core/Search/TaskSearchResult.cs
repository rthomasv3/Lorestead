namespace Lorestead.Core.Search
{
    public sealed class TaskSearchResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string ColumnId { get; set; }
        public string ColumnName { get; set; }
        public string BoardId { get; set; }
        public string BoardName { get; set; }
        public string UpdatedAt { get; set; }
    }
}
