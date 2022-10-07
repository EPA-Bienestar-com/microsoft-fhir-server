﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    /// <summary>
    /// Handles all communication between the API and SQL Server.
    /// </summary>
    public partial class SqlService
    {
        public const byte QueueType = 3;

        public bool JobQueueIsNotEmpty()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("SELECT count(*) FROM dbo.JobQueue WHERE QueueType = @QueueType", conn) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            var cnt = (int)command.ExecuteScalar();
            return cnt > 0;
        }

        private void DequeueJob(out long groupId, out long jobId, out long version, out string definition)
        {
            definition = null;
            groupId = -1L;
            jobId = -1L;
            version = 0;

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.DequeueJob", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            command.Parameters.AddWithValue("@HeartbeatTimeoutSec", 600);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                //// put job type here
                groupId = reader.GetInt64(0);
                jobId = reader.GetInt64(1);
                definition = reader.GetString(2);
                version = reader.GetInt64(3);
            }
        }

        public void DequeueJob(out long jobId, out long version, out string definition)
        {
            DequeueJob(out var _, out jobId, out version, out definition);
        }

        public void PutJobHeartbeat(long jobId, int? resourceCount = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobHeartbeat", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }

            command.ExecuteNonQuery();
        }

        public void CompleteJob(long jobId, bool failed, long version, int? resourceCount = null, int? totalCount = null, long? transactionId = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.PutJobStatus", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", QueueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Failed", failed);
            command.Parameters.AddWithValue("@RequestCancellationOnFailure", true);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@Data", DBNull.Value);
            }

            if (totalCount.HasValue || transactionId.HasValue)
            {
                var res = string.Empty;
                if (totalCount.HasValue)
                {
                    res = $"total={totalCount.Value}";
                }

                if (transactionId.HasValue)
                {
                    res = $"{res}{(res.Length == 0 ? string.Empty : " ")}tran={transactionId.Value}";
                }

                command.Parameters.AddWithValue("@FinalResult", res);
            }
            else
            {
                command.Parameters.AddWithValue("@FinalResult", DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}