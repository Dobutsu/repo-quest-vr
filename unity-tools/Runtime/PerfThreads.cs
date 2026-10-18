using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace RepoQuest
{
    /// <summary>
    /// For PerfLog: CPU time of the game's threads from /proc/self/task, user and kernel apart, with the core each ran on
    /// last and its scheduling policy, sampled once a second on a background thread; and the calls PerfLog's A/B test
    /// makes to move a thread to other cores or take its real-time priority away (on Pico 4 Ultra the busiest thread,
    /// the render thread, runs SCHED_FIFO: the runtime sets that through XR_KHR_android_thread_settings).
    /// Linux only; elsewhere the samples stay empty and the calls fail.
    /// </summary>
    public sealed class PerfThreads : IDisposable
    {
        public sealed class Sample
        {
            public int Tid;
            public string Name;
            public float User, Kernel;
            public int Core, Policy, RtPriority, Nice;
            public float Total => User + Kernel;
        }

        private const float TicksPerSecond = 100f;
        private const int SchedOther = 0;
        private const int ResetOnFork = 0x40000000;

        private readonly Thread thread;
        private volatile bool stop;
        private readonly object gate = new();
        private List<Sample> latest = new();
        private string system = "";
        private readonly Dictionary<int, (long user, long kernel)> previous = new();
        private long[] previousSystem;
        private long previousTime;

        public PerfThreads()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "RepoQuest PerfThreads" };
            thread.Start();
        }

        public void Dispose() => stop = true;

        /// <summary>The last second's threads, busiest first</summary>
        public List<Sample> Latest
        {
            get { lock (gate) return latest; }
        }

        /// <summary>One line: the busiest threads and the whole system's user/kernel/idle time</summary>
        public string Summary(int count)
        {
            List<Sample> samples;
            string all;
            lock (gate)
            {
                samples = latest;
                all = system;
            }
            if (samples.Count == 0)
                return "threads: -";
            return "threads: " + string.Join(", ", samples.Take(count).Select(s =>
                       $"{s.Name}#{s.Tid} {s.Total:0}% (sys {s.Kernel:0}) cpu{s.Core} {PolicyName(s.Policy)}" +
                       (s.Policy is 1 or 2 ? $"/{s.RtPriority}" : s.Nice != 0 ? $" n{s.Nice}" : ""))) +
                   (all.Length > 0 ? " | " + all : "");
        }

        private void Loop()
        {
            while (!stop)
            {
                try
                {
                    Measure();
                }
                catch (Exception) { }
                Thread.Sleep(1000);
            }
        }

        private void Measure()
        {
            var time = Stopwatch.GetTimestamp();
            var seconds = previousTime == 0 ? 0 : (time - previousTime) / (float)Stopwatch.Frequency;
            previousTime = time;
            var samples = new List<Sample>();
            var seen = new HashSet<int>();
            foreach (var dir in Directory.GetDirectories("/proc/self/task"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var tid))
                    continue;
                string stat;
                try
                {
                    stat = File.ReadAllText(Path.Combine(dir, "stat"));
                }
                catch (IOException)
                {
                    continue;
                }
                // "tid (name) state ...": the name may hold spaces and parentheses, the fields after the last ')' do not
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                if (open < 0 || close < open)
                    continue;
                var f = stat.Substring(close + 2).Split(' ');
                if (f.Length < 39)
                    continue;
                // proc(5) field n is f[n - 3]: utime 14, stime 15, nice 19, processor 39, rt_priority 40, policy 41
                var user = long.Parse(f[11]);
                var kernel = long.Parse(f[12]);
                seen.Add(tid);
                if (previous.TryGetValue(tid, out var before) && seconds > 0)
                    samples.Add(new Sample
                    {
                        Tid = tid,
                        Name = stat.Substring(open + 1, close - open - 1),
                        User = (user - before.user) / TicksPerSecond / seconds * 100f,
                        Kernel = (kernel - before.kernel) / TicksPerSecond / seconds * 100f,
                        Nice = int.Parse(f[16]),
                        Core = int.Parse(f[36]),
                        RtPriority = int.Parse(f[37]),
                        Policy = int.Parse(f[38]),
                    });
                previous[tid] = (user, kernel);
            }
            foreach (var gone in previous.Keys.Where(t => !seen.Contains(t)).ToList())
                previous.Remove(gone);
            samples.Sort((a, b) => b.Total.CompareTo(a.Total));

            var all = "";
            try
            {
                // cpu  user nice system idle iowait irq softirq ...
                var cpu = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).Take(7).Select(long.Parse).ToArray();
                if (previousSystem != null && seconds > 0)
                {
                    float Pct(long ticks) => ticks / TicksPerSecond / seconds * 100f;
                    var d = cpu.Zip(previousSystem, (a, b) => a - b).ToArray();
                    all = $"system user {Pct(d[0] + d[1]):0}% sys {Pct(d[2] + d[5] + d[6]):0}% idle {Pct(d[3] + d[4]):0}%";
                }
                previousSystem = cpu;
            }
            catch (Exception) { }

            lock (gate)
            {
                latest = samples;
                system = all;
            }
        }

        private static string PolicyName(int policy) => (policy & 0xff) switch
        {
            0 => "OTHER",
            1 => "FIFO",
            2 => "RR",
            3 => "BATCH",
            5 => "IDLE",
            var p => $"policy{p}",
        };

        // ---- What PerfLog reads once, and what its A/B test changes

        public static int CurrentTid()
        {
            try
            {
                return gettid();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static string ReadFile(string path)
        {
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>Cpus_allowed_list of a thread (0: the process)</summary>
        public static string AllowedCores(int tid = 0)
        {
            var status = ReadFile(tid == 0 ? "/proc/self/status" : $"/proc/self/task/{tid}/status");
            var line = status.Split('\n').FirstOrDefault(l => l.StartsWith("Cpus_allowed_list:"));
            return line?.Substring("Cpus_allowed_list:".Length).Trim() ?? "?";
        }

        /// <summary>Each core's top frequency, e.g. "0-1 2.05 GHz, 2-5 2.36 GHz"; and the mask of the fastest</summary>
        public static string Cores(out ulong fastest)
        {
            var max = new List<long>();
            for (var core = 0; core < 64; core++)
            {
                var text = ReadFile($"/sys/devices/system/cpu/cpu{core}/cpufreq/cpuinfo_max_freq");
                if (!long.TryParse(text, out var khz))
                {
                    if (!Directory.Exists($"/sys/devices/system/cpu/cpu{core}"))
                        break;
                    khz = 0;
                }
                max.Add(khz);
            }
            fastest = 0;
            if (max.Count == 0)
                return "?";
            var top = max.Max();
            for (var core = 0; core < max.Count; core++)
                if (max[core] == top)
                    fastest |= 1UL << core;
            var groups = new List<string>();
            for (var start = 0; start < max.Count;)
            {
                var end = start;
                while (end + 1 < max.Count && max[end + 1] == max[start])
                    end++;
                groups.Add($"{(start == end ? $"{start}" : $"{start}-{end}")} {max[start] / 1e6:0.00} GHz");
                start = end + 1;
            }
            return string.Join(", ", groups);
        }

        public static bool GetAffinity(int tid, out ulong mask, out string error)
        {
            mask = 0;
            error = "";
            if (sched_getaffinity(tid, (IntPtr)sizeof(ulong), ref mask) == 0)
                return true;
            error = $"errno {Marshal.GetLastWin32Error()}";
            return false;
        }

        public static bool SetAffinity(int tid, ulong mask, out string error)
        {
            error = "";
            if (sched_setaffinity(tid, (IntPtr)sizeof(ulong), ref mask) == 0)
                return true;
            error = $"errno {Marshal.GetLastWin32Error()}";
            return false;
        }

        public static bool GetScheduler(int tid, out int policy, out int priority)
        {
            priority = 0;
            policy = sched_getscheduler(tid);
            return policy >= 0 && sched_getparam(tid, ref priority) == 0;
        }

        public static bool SetScheduler(int tid, int policy, int priority, out string error)
        {
            error = "";
            if (sched_setscheduler(tid, policy, ref priority) == 0)
                return true;
            error = $"errno {Marshal.GetLastWin32Error()}";
            return false;
        }

        /// <summary>SCHED_OTHER, keeping SCHED_RESET_ON_FORK: clearing that flag needs CAP_SYS_NICE (EPERM otherwise)</summary>
        public static bool SetNormalScheduler(int tid, int policy, out string error) =>
            SetScheduler(tid, SchedOther | (policy & ResetOnFork), 0, out error);

        public static string Mask(ulong mask) =>
            string.Join(",", Enumerable.Range(0, 64).Where(c => (mask & (1UL << c)) != 0));

        [DllImport("libc")]
        private static extern int gettid();

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_getaffinity(int pid, IntPtr size, ref ulong mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_setaffinity(int pid, IntPtr size, ref ulong mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_getscheduler(int pid);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_getparam(int pid, ref int priority);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_setscheduler(int pid, int policy, ref int priority);
    }
}
