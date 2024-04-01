﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevHome.Common.Contracts.Services;
using DevHome.Common.Environments.Models;
using DevHome.Common.Models;
using Microsoft.Windows.DevHome.SDK;
using Serilog;
using Windows.Foundation;

namespace DevHome.Common.Environments.Services;

/// <summary>
/// Service thats used to get the ComputeSystems from the providers so they can be loaded into the UI.
/// This class is also used to keep track of the ComputeSystem that a configuration file will be applied to.
/// </summary>
public class ComputeSystemManager : IComputeSystemManager
{
    private readonly ILogger _log = Log.ForContext("SourceContext", nameof(ComputeSystemManager));

    private readonly IComputeSystemService _computeSystemService;

    private readonly List<CreateComputeSystemOperation> _createComputeSystemOperations = new();

    public event TypedEventHandler<ComputeSystem, ComputeSystemState> ComputeSystemStateChanged = (sender, state) => { };

    private object _creationOperationLock = new();

    // Used in the setup flow to store the ComputeSystem needed to configure.
    public ComputeSystemReviewItem? ComputeSystemSetupItem { get;  set; }

    public ComputeSystemManager(IComputeSystemService computeSystemService)
    {
        _computeSystemService = computeSystemService;
    }

    /// <summary>
    /// This method gets the ComputeSystems from the providers in parallel.
    /// </summary>
    public async Task GetComputeSystemsAsync(Func<ComputeSystemsLoadedData, Task> callback)
    {
        // Create a cancellation token that will cancel the task after 2 minute.
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(2));
        var token = cancellationTokenSource.Token;
        var computeSystemsProviderDetails = await _computeSystemService.GetComputeSystemProvidersAsync();

        try
        {
            // get compute systems from providers in parallel.
            await Parallel.ForEachAsync(computeSystemsProviderDetails, async (providerDetails, token) =>
            {
                var provider = providerDetails.ComputeSystemProvider;
                var devIdWrappers = new List<DeveloperIdWrapper>();
                var results = new List<ComputeSystemsResult>();
                var wrapperDictionary = new Dictionary<DeveloperIdWrapper, ComputeSystemsResult>();

                foreach (var devIdWrapper in providerDetails.DeveloperIds)
                {
                    var result = await providerDetails.ComputeSystemProvider.GetComputeSystemsAsync(devIdWrapper.DeveloperId);
                    wrapperDictionary.Add(devIdWrapper, result);
                    results.Add(result);
                }

                var loadedData = new ComputeSystemsLoadedData(providerDetails, wrapperDictionary);
                await callback(loadedData);
            });
        }
        catch (AggregateException aggregateEx)
        {
            foreach (var innerEx in aggregateEx.InnerExceptions)
            {
                if (innerEx is TaskCanceledException)
                {
                    _log.Error($"Failed to get retrieve all compute systems from all compute system providers due to cancellation", innerEx);
                }
                else
                {
                    _log.Error($"Failed to get retrieve all compute systems from all compute system providers ", innerEx);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to get retrieve all compute systems from all compute system providers ", ex);
        }
    }

    public void OnComputeSystemStateChanged(ComputeSystem sender, ComputeSystemState state)
    {
        ComputeSystemStateChanged(sender, state);
    }

    public List<CreateComputeSystemOperation> GetRunningOperationsForCreation()
    {
        lock (_creationOperationLock)
        {
            return _createComputeSystemOperations;
        }
    }

    public void AddRunningOperationForCreation(CreateComputeSystemOperation operation)
    {
        lock (_creationOperationLock)
        {
            _createComputeSystemOperations.Add(operation);
        }
    }

    public void RemoveRunningOperationForCreation(Guid operationId)
    {
        lock (_creationOperationLock)
        {
            for (var i = _createComputeSystemOperations.Count - 1; i >= 0; i--)
            {
                var operation = _createComputeSystemOperations[i];
                if (operation.OperationId == operationId)
                {
                    _createComputeSystemOperations.RemoveAt(i);
                    break;
                }
            }
        }
    }
}
