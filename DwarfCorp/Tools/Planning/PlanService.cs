// PlanService.cs
// 
//  Modified MIT License (MIT)
//  
//  Copyright (c) 2015 Completely Fair Games Ltd.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// The following content pieces are considered PROPRIETARY and may not be used
// in any derivative works, commercial or non commercial, without explicit 
// written permission from Completely Fair Games:
// 
// * Images (sprites, textures, etc.)
// * 3D Models
// * Sound Effects
// * Music
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DwarfCorp
{
    /// <summary>
    /// A request to plan from point A to point B
    /// </summary>
    public class AstarPlanRequest
    {
        public PlanSubscriber Subscriber;
        public CreatureAI Sender;
        public VoxelHandle Start;
        public int MaxExpansions;
        public GoalRegion GoalRegion;
        public float HeuristicWeight = 1;
        public int ID;
        public static int MaxId = 0;
        public AstarPlanRequest()
        {
            ID = MaxId;
            MaxId++;
        }
    }

    /// <summary>
    /// The result of a plan request (has a path on success)
    /// </summary>
    public class AStarPlanResponse
    {
        public bool Success;
        public List<MoveAction> Path;
        public AstarPlanRequest Request;
        public AStarPlanner.PlanResultCode Result;
    }

    /// <summary>
    /// A service call which plans from pointA to pointB voxels.
    /// </summary>
    public class PlanService : Service<AstarPlanRequest, AStarPlanResponse>
    {
        public PlanService() : base("Path Planner", GameSettings.Current.NumPathingThreads)
        {
            // Wire up queue-depth sampler so PerfCounters.SnapshotIntoMetrics can
            // read Requests.Count without a hard dependency from Tools/ → Planning/.
            PerfCounters.PathfindingQueueDepthSampler = () => Requests.Count;
        }

        public override AStarPlanResponse HandleRequest(AstarPlanRequest req)
        {
            Interlocked.Increment(ref PerfCounters.PlansStarted);

            // If there are no subscribers that want this request, it must be old. So remove it.
            if (Subscribers.Find(s => s.ID == req.Subscriber.ID) == null)
            {
                Interlocked.Increment(ref PerfCounters.PlansCancelled);
                return new AStarPlanResponse
                {
                    Path = null,
                    Success = false,
                    Request = req,
                    Result = AStarPlanner.PlanResultCode.Cancelled
                };
            }

            AStarPlanner.PlanResultCode result;
            var sw = Stopwatch.StartNew();
            List<MoveAction> path = AStarPlanner.FindPath(req.Sender.Movement, req.Start, req.GoalRegion, req.Sender.Manager.World.ChunkManager,
                req.MaxExpansions, req.HeuristicWeight, Requests.Count, () => { return Subscribers.Find(s => s.ID == req.Subscriber.ID && s.CurrentRequestID == req.ID) != null; }, out result);
            sw.Stop();
            long micros = sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;

            // Attribute the result — worker threads bump atomic counters; main thread
            // snapshots + averages once per frame via PerfCounters.SnapshotIntoMetrics.
            Interlocked.Add(ref PerfCounters.PlansMicrosSum, micros);
            Interlocked.Increment(ref PerfCounters.PlansCompletedThisAccum);
            PerfCounters.UpdateMax(ref PerfCounters.PlansMaxMicrosSession, micros);
            switch (result)
            {
                case AStarPlanner.PlanResultCode.Success:
                    Interlocked.Increment(ref PerfCounters.PlansSucceeded);
                    break;
                case AStarPlanner.PlanResultCode.Cancelled:
                    Interlocked.Increment(ref PerfCounters.PlansCancelled);
                    break;
                default:
                    // Invalid, NoSolution, MaxExpansionsReached all count as "failed".
                    Interlocked.Increment(ref PerfCounters.PlansFailed);
                    break;
            }

            AStarPlanResponse res = new AStarPlanResponse
            {
                Path = path,
                Success = (path != null),
                Request = req,
                Result = result
            };

            return res;
        }

        public override bool AddRequest(AstarPlanRequest request, uint subscriberID)
        {
            Interlocked.Increment(ref PerfCounters.PlansQueued);
            return base.AddRequest(request, subscriberID);
        }
    }

}
