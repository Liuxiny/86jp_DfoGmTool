using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        /// <summary>
        /// 删除当前角色收件箱(folder=0)中的收件人行。
        /// 共享邮件只有在没有任何 recipient 时才删除根消息；根消息删除
        /// 依赖 schema 外键级联附件，并让 campaign delivery 的 message_id
        /// 通过 ON DELETE SET NULL 保持可审计。
        /// </summary>
        public object ClearCharacterMailbox(int characterId)
        {
            if (characterId <= 0)
                return Error("角色编号无效");

            try
            {
                using var connection = new SqliteConnection(_config.ConnectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction(deferred: false);

                if (!CharacterExists(connection, transaction, characterId))
                    return Error("角色不存在或已删除: " + characterId);

                var messageIds = LoadFolderMessages(connection, transaction, characterId);
                var removedRecipients = 0;
                var removedMessages = 0;
                var removedAttachments = 0;
                var removedAudits = 0;
                var nulledCampaignReferences = 0;

                foreach (var messageId in messageIds)
                {
                    using (var deleteRecipient = connection.CreateCommand())
                    {
                        deleteRecipient.Transaction = transaction;
                        deleteRecipient.CommandText = @"
DELETE FROM mailbox_recipients
WHERE message_id=@messageId AND character_id=@cid AND folder=0;";
                        deleteRecipient.Parameters.AddWithValue("@messageId", messageId);
                        deleteRecipient.Parameters.AddWithValue("@cid", characterId);
                        removedRecipients += deleteRecipient.ExecuteNonQuery();
                    }

                    // 其它角色/其它 folder 仍持有该消息时，只删除当前
                    // recipient 行，根消息及其附件/审计都必须保留。
                    if (CountMessageRecipients(connection, transaction, messageId) != 0)
                        continue;

                    removedAttachments += CountRows(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM mailbox_attachments WHERE message_id=@messageId;",
                        messageId);
                    removedAudits += CountRows(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM mailbox_system_mail_audit WHERE message_id=@messageId;",
                        messageId);
                    nulledCampaignReferences += CountRows(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM mailbox_campaign_deliveries WHERE message_id=@messageId;",
                        messageId);

                    // mailbox_system_mail_audit 没有指向 mailbox_messages 的
                    // 外键，先显式清理审计及其附件，避免留下孤立记录。
                    ExecuteDelete(
                        connection,
                        transaction,
                        "DELETE FROM mailbox_system_mail_audit_attachments WHERE audit_id IN (SELECT audit_id FROM mailbox_system_mail_audit WHERE message_id=@messageId);",
                        messageId);
                    ExecuteDelete(
                        connection,
                        transaction,
                        "DELETE FROM mailbox_system_mail_audit WHERE message_id=@messageId;",
                        messageId);

                    // 根消息删除由外键级联 mailbox_recipients/attachments；
                    // mailbox_campaign_deliveries.message_id 会 SET NULL。
                    ExecuteDelete(
                        connection,
                        transaction,
                        "DELETE FROM mailbox_messages WHERE message_id=@messageId;",
                        messageId);
                    removedMessages++;
                }

                transaction.Commit();
                return new
                {
                    success = true,
                    characterId,
                    folder = 0,
                    recipientCount = removedRecipients,
                    messageCount = removedMessages,
                    attachmentCount = removedAttachments,
                    auditCount = removedAudits,
                    campaignReferenceCount = nulledCampaignReferences,
                };
            }
            catch (SqliteException ex)
            {
                return Error("清空邮箱失败: " + ex.Message);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is OverflowException)
            {
                return Error("清空邮箱失败: " + ex.Message);
            }
        }

        private static bool CharacterExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM characters WHERE character_id=@cid AND delete_flag=0;";
            command.Parameters.AddWithValue("@cid", characterId);
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }

        private static List<long> LoadFolderMessages(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            var result = new List<long>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT message_id
FROM mailbox_recipients
WHERE character_id=@cid AND folder=0
ORDER BY message_id;";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetInt64(0));
            return result;
        }

        private static int CountMessageRecipients(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long messageId)
        {
            return CountRows(
                connection,
                transaction,
                "SELECT COUNT(*) FROM mailbox_recipients WHERE message_id=@messageId;",
                messageId);
        }

        private static int CountRows(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            long messageId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@messageId", messageId);
            return checked((int)Convert.ToInt64(command.ExecuteScalar()));
        }

        private static void ExecuteDelete(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            long messageId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@messageId", messageId);
            command.ExecuteNonQuery();
        }

    }
}
