﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Elsa.Activities.AzureServiceBus.Services;
using Elsa.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Activities.AzureServiceBus.StartupTasks
{
    public class StartServiceBusQueues : BackgroundService
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly IWorkflowBlueprintReflector _workflowBlueprintReflector;
        private readonly IMessageReceiverFactory _messageReceiverFactory;
        private readonly IServiceProvider _serviceProvider;

        public StartServiceBusQueues(IWorkflowRegistry workflowRegistry, IWorkflowBlueprintReflector workflowBlueprintReflector, IMessageReceiverFactory messageReceiverFactory, IServiceProvider serviceProvider)
        {
            _workflowRegistry = workflowRegistry;
            _workflowBlueprintReflector = workflowBlueprintReflector;
            _messageReceiverFactory = messageReceiverFactory;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cancellationToken = stoppingToken;
            var queueNames = await GetQueueNamesAsync(cancellationToken).ToListAsync(cancellationToken);

            foreach (var queueName in queueNames)
            {
                var receiver = await _messageReceiverFactory.GetReceiverAsync(queueName, cancellationToken);
                ActivatorUtilities.CreateInstance<QueueWorker>(_serviceProvider, receiver);
            }
        }

        private async IAsyncEnumerable<string> GetQueueNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var workflows = await _workflowRegistry.GetWorkflowsAsync(cancellationToken).ToListAsync(cancellationToken);

            var query =
                from workflow in workflows
                from activity in workflow.Activities
                where activity.Type == nameof(AzureServiceBusMessageReceived)
                select workflow;

            foreach (var workflow in query)
            {
                var workflowBlueprintWrapper = await _workflowBlueprintReflector.ReflectAsync(workflow, cancellationToken);

                foreach (var activity in workflowBlueprintWrapper.Filter<AzureServiceBusMessageReceived>())
                {
                    var queueName = await activity.GetPropertyValueAsync(x => x.QueueName, cancellationToken);
                    yield return queueName;
                }
            }
        }
    }
}