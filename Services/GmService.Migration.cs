namespace DfoGmTool.Services
{
    public sealed class A12ToA21MigrationRequest
    {
        public string DatabasePath { get; set; }
        public string PvfPath { get; set; }
        public bool UserBackedUp { get; set; }
        public string ConfirmText { get; set; }
    }
}
