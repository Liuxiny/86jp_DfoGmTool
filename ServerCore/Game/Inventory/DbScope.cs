using System;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class DbScope : IDisposable
    {
        public SqliteConnection Connection { get; }
        public SqliteTransaction Transaction { get; }
        public int CharacterId { get; }
        public int AccountId { get; }

        internal DbScope(string connectionString, int characterId, int accountId)
        {
            CharacterId = characterId;
            AccountId = accountId;
            Connection = new SqliteConnection(connectionString);
            Connection.Open();
            Transaction = Connection.BeginTransaction();
        }

        public void Commit() => Transaction.Commit();

        public void Dispose()
        {
            Transaction?.Dispose();
            Connection?.Dispose();
        }
    }
}
