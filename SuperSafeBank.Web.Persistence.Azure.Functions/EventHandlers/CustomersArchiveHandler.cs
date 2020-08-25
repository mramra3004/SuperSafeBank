﻿using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using SuperSafeBank.Core.EventBus;
using SuperSafeBank.Domain.Events;
using SuperSafeBank.Persistence.Azure;
using SuperSafeBank.Web.Core.Queries.Models;

namespace SuperSafeBank.Web.Persistence.Azure.Functions.EventHandlers
{
    public class CustomersArchiveHandler : 
        INotificationHandler<EventReceived<CustomerCreated>>,
        INotificationHandler<EventReceived<AccountCreated>>
    {
        private readonly ILogger<CustomersArchiveHandler> _logger;
        private readonly Container _archiveContainer;

        public CustomersArchiveHandler(IDbContainerProvider containerProvider, ILogger<CustomersArchiveHandler> logger)
        {
            if (containerProvider == null) 
                throw new ArgumentNullException(nameof(containerProvider));

            _logger = logger;
            
            _archiveContainer = containerProvider.GetContainer("CustomersArchive");
        }

        public async Task Handle(EventReceived<CustomerCreated> @event, CancellationToken cancellationToken)
        {
            _logger.LogInformation("creating customer archive item for aggregate {AggregateId} ...", @event.Event.AggregateId);

            var partitionKey = new PartitionKey(@event.Event.AggregateId.ToString());

            try
            {
                var item = new
                {
                    id = @event.Event.AggregateId,
                    Firstname = @event.Event.Firstname,
                    Lastname = @event.Event.Lastname,
                    Version = @event.Event.AggregateVersion,
                };

                var response = await _archiveContainer.UpsertItemAsync(item, partitionKey);
                if (response.StatusCode != HttpStatusCode.Created)
                {
                    var msg = $"an error has occurred while processing an event: {response.Diagnostics}";
                    throw new Exception(msg);
                }

                _logger.LogInformation($"created customer archive item {@event.Event.AggregateId}");
            }
            catch (Exception e)
            {
                var msg = $"an error has occurred while processing an event: {e.Message}";
                _logger.LogError(e, msg);
                throw;
            }
         
        }

        public async Task Handle(EventReceived<AccountCreated> @event, CancellationToken cancellationToken)
        {
            var partitionKey = new PartitionKey(@event.Event.AggregateId.ToString());

            var customer = await _archiveContainer.ReadItemAsync<CustomerArchiveItem>(@event.Event.AggregateId.ToString(),
                                                                                    partitionKey, null, cancellationToken);

            if(null == customer)
                throw new ArgumentException($"invalid aggregate id: {@event.Event.AggregateId}");

            var accounts = customer.Resource.Accounts.ToList();
            accounts.Add(@event.Event.AggregateId);

            await _archiveContainer.UpsertItemAsync(new
            {
                Accounts = accounts
            }, partitionKey, null, cancellationToken);

            _logger.LogInformation($"updated customer archive item accounts {@event.Event.AggregateId}");
        }
    }
}