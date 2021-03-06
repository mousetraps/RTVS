﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core.Disposables;

namespace Microsoft.R.Host.Client.Test.Mocks {
    public sealed class RSessionMock : IRSession {
        private IRSessionInteraction _inter;

        public string LastExpression { get; private set; }
        public RSessionEvaluationMock Evaluation { get; private set; }

        public int Id { get; set; }
        public int? ProcessId { get; set; }
        public bool IsHostRunning { get; set; }

        public Task HostStarted => IsHostRunning ? Task.FromResult(0) : Task.FromCanceled(new CancellationToken(true));

        public string Prompt { get; set; } = ">";

        public Task<REvaluationResult> EvaluateAsync(string expression, REvaluationKind kind, CancellationToken ct = default(CancellationToken)) {
            LastExpression = expression;
            if (kind.HasFlag(REvaluationKind.Mutating)) {
                Mutated?.Invoke(this, EventArgs.Empty);
            }
            return Task.FromResult(new REvaluationResult());
        }

        public Task<IRSessionEvaluation> BeginEvaluationAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            Evaluation = new RSessionEvaluationMock();
            BeforeRequest?.Invoke(this, new RRequestEventArgs(Evaluation.Contexts, Prompt, 4096, true));
            if (Evaluation.IsMutating) {
                Mutated?.Invoke(this, EventArgs.Empty);
            }
            return Task.FromResult((IRSessionEvaluation)Evaluation);
        }

        public Task<IRSessionInteraction> BeginInteractionAsync(bool isVisible = true, CancellationToken cancellationToken = default (CancellationToken)) {
            _inter = new RSessionInteractionMock();
            BeforeRequest?.Invoke(this, new RRequestEventArgs(_inter.Contexts, Prompt, 4096, true));
            return Task.FromResult(_inter);
        }

        public Task CancelAllAsync() {
            if (Evaluation != null) {
                AfterRequest?.Invoke(this, new RRequestEventArgs(Evaluation.Contexts, Prompt, 4096, true));
                Evaluation = null;
            }
            else if (_inter != null) {
                AfterRequest?.Invoke(this, new RRequestEventArgs(_inter.Contexts, Prompt, 4096, true));
                _inter = null;
            }
            return Task.CompletedTask;
        }

        public void Dispose() {
            StopHostAsync().Wait(5000);
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public IDisposable DisableMutatedOnReadConsole() => Disposable.Empty;

        public void FlushLog() {
        }

        public Task StartHostAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout = 3000) {
            IsHostRunning = true;
            Connected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task StopHostAsync() {
            IsHostRunning = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

#pragma warning disable 67
        public event EventHandler<RRequestEventArgs> AfterRequest;
        public event EventHandler<RRequestEventArgs> BeforeRequest;
        public event EventHandler<EventArgs> Connected;
        public event EventHandler<EventArgs> DirectoryChanged;
        public event EventHandler<EventArgs> Disconnected;
        public event EventHandler<EventArgs> Disposed;
        public event EventHandler<EventArgs> Mutated;
        public event EventHandler<ROutputEventArgs> Output;
    }
}
