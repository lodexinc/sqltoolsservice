//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SqlTools.EditorServices.Utility;
using Microsoft.SqlTools.ServiceLayer.Hosting;
using Microsoft.SqlTools.ServiceLayer.Hosting.Protocol;
using Microsoft.SqlTools.ServiceLayer.ConnectionServices.Contracts;

namespace Microsoft.SqlTools.ServiceLayer.ConnectionServices
{
    /// <summary>
    /// Main class for the Connection Management services
    /// </summary>
    public class ConnectionService
    {
        #region Singleton Instance Implementation

        /// <summary>
        /// Singleton service instance
        /// </summary>
        private static Lazy<ConnectionService> instance 
            = new Lazy<ConnectionService>(() => new ConnectionService());

        /// <summary>
        /// Gets the singleton service instance
        /// </summary>
        public static ConnectionService Instance
        {
            get
            {
                return instance.Value;
            }
        }

        /// <summary>
        /// Default constructor is private since it's a singleton class
        /// </summary>
        private ConnectionService()
        {
        }

        #endregion

        #region Properties

        /// <summary>
        /// The SQL connection factory object
        /// </summary>
        private ISqlConnectionFactory connectionFactory;

        /// <summary>
        /// The current connection id that was previously used
        /// </summary>
        private int maxConnectionId = 0;

        /// <summary>
        /// Active connections lazy dictionary instance
        /// </summary>
        private Lazy<Dictionary<int, ISqlConnection>> activeConnections
            = new Lazy<Dictionary<int, ISqlConnection>>(() 
                => new Dictionary<int, ISqlConnection>());
          
        /// <summary>
        /// Callback for onconnection handler
        /// </summary>
        /// <param name="sqlConnection"></param>
        public delegate Task OnConnectionHandler(ISqlConnection sqlConnection); 

        /// <summary>
        /// List of onconnection handlers
        /// </summary>
        private readonly List<OnConnectionHandler> onConnectionActivities = new List<OnConnectionHandler>(); 

        /// <summary>
        /// Gets the active connection map
        /// </summary>
        public Dictionary<int, ISqlConnection> ActiveConnections
        {
            get
            {
                return activeConnections.Value;
            }
        }

        /// <summary>
        /// Gets the SQL connection factory instance
        /// </summary>
        public ISqlConnectionFactory ConnectionFactory
        {
            get
            {
                if (this.connectionFactory == null)
                {
                    this.connectionFactory = new SqlConnectionFactory();
                }
                return this.connectionFactory;
            }
        }

        #endregion

        /// <summary>
        /// Test constructor that injects dependency interfaces
        /// </summary>
        /// <param name="testFactory"></param>
        public ConnectionService(ISqlConnectionFactory testFactory)
        {
            this.connectionFactory = testFactory;
        }

        #region Public Methods

        /// <summary>
        /// Open a connection with the specified connection details
        /// </summary>
        /// <param name="connectionDetails"></param>
        public async Task<ConnectionResult> Connect(ConnectionDetails connectionDetails)
        {
            // build the connection string from the input parameters
            string connectionString = BuildConnectionString(connectionDetails);

            // create a sql connection instance
            ISqlConnection connection = ConnectionFactory.CreateSqlConnection(connectionString);

            // open the database
            await connection.OpenAsync();

            // map the connection id to the connection object for future lookups
            ActiveConnections.Add(++maxConnectionId, connection);

            // invoke callback notifications
            var onConnectionCallbackTasks = onConnectionActivities.Select(t => t(connection));
            await Task.WhenAll(onConnectionCallbackTasks);
            // TODO: Evaulate if we want to avoid waiting here. We'll need error handling on the other side if we don't wait

            // return the connection result
            return new ConnectionResult()
            {
                ConnectionId = maxConnectionId
            };
        }

        public void Initialize(ServiceHost serviceHost)
        {
            // Register request and event handlers with the Service Host
            serviceHost.SetRequestHandler(ConnectionRequest.Type, HandleConnectRequest);
        }

        /// <summary> 
        /// Add a new method to be called when the onconnection request is submitted 
        /// </summary> 
        /// <param name="activity"></param> 
        public void RegisterOnConnectionTask(OnConnectionHandler activity) 
        { 
            onConnectionActivities.Add(activity); 
        }

        #endregion

        #region Request Handlers

        /// <summary>
        /// Handle new connection requests
        /// </summary>
        /// <param name="connectionDetails"></param>
        /// <param name="requestContext"></param>
        /// <returns></returns>
        protected async Task HandleConnectRequest(
            ConnectionDetails connectionDetails,
            RequestContext<ConnectionResult> requestContext)
        {
            Logger.Write(LogLevel.Verbose, "HandleConnectRequest");

            // open connection base on request details
            ConnectionResult result = await Connect(connectionDetails);

            await requestContext.SendResult(result);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Build a connection string from a connection details instance
        /// </summary>
        /// <param name="connectionDetails"></param>
        private string BuildConnectionString(ConnectionDetails connectionDetails)
        {
            SqlConnectionStringBuilder connectionBuilder = new SqlConnectionStringBuilder();
            connectionBuilder["Data Source"] = connectionDetails.ServerName;
            connectionBuilder["Integrated Security"] = false;
            connectionBuilder["User Id"] = connectionDetails.UserName;
            connectionBuilder["Password"] = connectionDetails.Password;
            connectionBuilder["Initial Catalog"] = connectionDetails.DatabaseName;
            return connectionBuilder.ToString();
        }

        #endregion
    }
}
