using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Utilities
{
    public class AFDataObserver : IObserver<AFDataPipeEvent>, IDisposable
    {
        private Action<AFValue> _onNextAction;
        private IList<AFAttribute> _attributes;
        private AFDataPipe _dataPipe;
        private bool _disposed = false;
        private CancellationTokenSource _tokenSource;
        private int _threadSleepTimeInMilliseconds = 5000;
        private CancellationToken _ct;
        private Task _mainTask;

        public AFDataObserver(IList<AFAttribute> attributes, Action<AFValue> onNextAction)
        {
            _attributes = attributes;
            _onNextAction = onNextAction;
            _dataPipe = new AFDataPipe();
            _tokenSource = new CancellationTokenSource();
            _ct = _tokenSource.Token;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException("AFDataObserver was disposed.");

            Console.WriteLine("{0} | Signing up for updates for {1} attributes", DateTime.Now, _attributes.Count);
            _dataPipe.Subscribe(this);
            
            // Throw exception on errors
            var errors = _dataPipe.AddSignups(_attributes);
            if (errors != null)
            {
                throw new Exception(errors.ToString(), new AggregateException(errors.Errors.Values));
            }

            Console.WriteLine("{0} | Signed up for updates for {1} attributes\n", DateTime.Now, _attributes.Count);

            _mainTask = Task.Factory.StartNew(() =>
             {
                 Boolean hasMoreEvents = false;

                 while (true)
                 {
                     if (_ct.IsCancellationRequested)
                     {
                         // The main task in AFDataObserver is cancelled.
                         _ct.ThrowIfCancellationRequested();
                     }

                     // NOTE!!! A "OperationCanceledException was unhandled  
                     // by user code" error will be raised here if "Just My Code" 
                     // is enabled on your computer. On Express editions JMC is  
                     // enabled and cannot be disabled. The exception is benign.  
                     // Just press F5 to continue executing your code.  
                     var results = _dataPipe.GetObserverEvents(out hasMoreEvents);

                     if (results != null)
                     {
                         Console.WriteLine("Errors in GetObserverEvents: {0}", results.ToString());
                     }

                     if (!hasMoreEvents)
                         Thread.Sleep(_threadSleepTimeInMilliseconds);
                 }
             }, _ct);
        }

        public void OnCompleted()
        {
            Console.WriteLine("{0} | PI DataPipe was terminated", DateTime.Now);
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(AFDataPipeEvent value)
        {
            _onNextAction(value.Value);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_tokenSource != null)
                {
                    _tokenSource.Cancel();

                    try
                    {
                        if (_mainTask != null)
                        {
                            _mainTask.Wait();
                        }
                    }
                    catch (AggregateException e)
                    {
                        foreach (var v in e.InnerExceptions)
                        {
                            if (!(v is TaskCanceledException))
                                Console.WriteLine("Exception in the main task : {0}", v);
                        }
                        Console.WriteLine();
                    }
                    finally
                    {
                        _tokenSource.Dispose();
                    }
                }

                if (_dataPipe != null)
                    _dataPipe.Dispose();
            }

            _tokenSource = null;
            _dataPipe = null;
            _disposed = true;
        }
    }
}
