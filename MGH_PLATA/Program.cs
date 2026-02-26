using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;

// Defined class to represent a patient/service entry
public class Patient
{
    public int PatientNumber { get; set; }
    public string Service { get; set; }
    public string Acuity { get; set; }
    public DateTime? Arrival { get; set; } // Nullable DateTime
    public int? LOS { get; set; } // Nullable int

    // Constructor without Arrival and LOS
    public Patient(int patientNumber, string service, string acuity)
    {
        PatientNumber = patientNumber;
        Service = service;
        Acuity = acuity;
        Arrival = null; // Initially empty
        LOS = null; // Initially empty
    }

    // Optional: Constructor with Arrival and LOS for future use
    public Patient(int patientNumber, string service, string acuity, DateTime arrival, int los)
    {
        PatientNumber = patientNumber;
        Service = service;
        Acuity = acuity;
        Arrival = arrival;
        LOS = los;
    }
}

internal sealed class WorkerState
{
    public WorkerState(string name, int active, DateTime endTime)
    {
        Name = name;
        Active = active;
        EndTime = endTime;
    }

    public string Name { get; }
    public int Active { get; set; }
    public int OnLunch { get; set; }
    public int HadLunch { get; set; }
    public DateTime EndTime { get; set; }
    public Queue<Patient> Queue { get; } = new Queue<Patient>();
}

internal sealed class StageState
{
    public StageState(
        string name,
        Queue<Patient> inputQueue,
        Queue<Patient> outputQueue,
        int maxOnLunch,
        int lunchQueueThreshold,
        bool usesExamRooms,
        bool requireEndTimeBeforeStart,
        bool usesNursePaperworkCompletion,
        Func<Patient, Random, int?> computeAddedMinutes)
    {
        Name = name;
        InputQueue = inputQueue;
        OutputQueue = outputQueue;
        MaxOnLunch = maxOnLunch;
        LunchQueueThreshold = lunchQueueThreshold;
        UsesExamRooms = usesExamRooms;
        RequireEndTimeBeforeStart = requireEndTimeBeforeStart;
        UsesNursePaperworkCompletion = usesNursePaperworkCompletion;
        ComputeAddedMinutes = computeAddedMinutes;
    }

    public string Name { get; }
    public Queue<Patient> InputQueue { get; }
    public Queue<Patient> OutputQueue { get; }
    public int MaxOnLunch { get; }
    public int LunchQueueThreshold { get; }
    public bool UsesExamRooms { get; }
    public bool RequireEndTimeBeforeStart { get; }
    public bool UsesNursePaperworkCompletion { get; }
    public int CapacityPerWorker { get; } = 1;
    public Func<Patient, Random, int?> ComputeAddedMinutes { get; }
    public List<WorkerState> Workers { get; } = new List<WorkerState>();
}

internal static class Program
{
    private const int RnPaperworkMinutes = 11;
    private const string DbConnectionEnvVar = "MGH_DB_CONNECTION";

    static List<Patient> scheduled_patient = new List<Patient>();
    static DateTime startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 7, 0, 0);
    static DateTime currentTime = startTime;
    static DateTime endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23, 50, 0);
    static int arrivalSpan = 10;
    static int examRooms = 12;
    static int examRooms_Occ = 0;
    static int simulatedCase = 1;

    static DateTime lunchStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 11, 0, 0);
    static DateTime lunchEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 14, 0, 0);
    static int lunchDuration = 30;
    static string dbConnectionString = string.Empty;

    static int ignoreNursePPwrk = 0;
    static int nursePPwrk = 11;

    // WaitRoom1
    static Queue<Patient> waitRoomQueue = new Queue<Patient>();
    // WaitRoom2
    static Queue<Patient> waitRoomQueue_Vitals = new Queue<Patient>();
    // WaitRoom3
    static Queue<Patient> waitRoomQueue_Nurse = new Queue<Patient>();
    // WaitRoom4
    static Queue<Patient> waitRoomQueue_MD = new Queue<Patient>();
    // WaitRoom5
    static Queue<Patient> waitRoomQueue_Blood = new Queue<Patient>();
    // WaitRoom6
    static Queue<Patient> waitRoomQueue_checkOut = new Queue<Patient>();

    static StageState attendStage = null!;
    static StageState vitalsStage = null!;
    static StageState rnStage = null!;
    static StageState mdStage = null!;
    static StageState bloodStage = null!;
    static List<StageState> allStages = new List<StageState>();

    static Random globalRandom = new Random();

    static readonly int[] attendActive = { 1, 1, 0 };
    static readonly int[] vitalsActive = { 1, 1, 0 };
    static readonly int[] rnActive = { 1, 1, 1, 1, 1, 0 };
    static readonly int[] mdActive = { 1, 1, 1, 1, 1, 1, 1, 1, 0 };
    static readonly int[] bloodActive = { 1, 1, 1, 0 };

    static readonly Dictionary<int, (string Service, string Acuity)> patientCatalog =
        new Dictionary<int, (string Service, string Acuity)>
        {
            [1] = ("NEUR", "High"),
            [2] = ("NEUR", "High"),
            [3] = ("GYN", "Low"),
            [4] = ("THOR", "Medium"),
            [5] = ("GYN", "Low"),
            [6] = ("PLAS", "Low"),
            [7] = ("SONC", "High"),
            [8] = ("GENS", "High"),
            [9] = ("TRNS", "Medium"),
            [10] = ("NEUR", "High"),
            [11] = ("GENS", "High"),
            [12] = ("ORTH", "Medium"),
            [13] = ("THOR", "Medium"),
            [14] = ("THOR", "Medium"),
            [15] = ("TRNS", "Medium"),
            [16] = ("NEUR", "High"),
            [17] = ("GENS", "High"),
            [18] = ("ANES", "Low"),
            [19] = ("UROL", "Medium"),
            [20] = ("UROL", "Medium"),
            [21] = ("GYN", "Low"),
            [22] = ("GENS", "High"),
            [23] = ("NEUR", "High"),
            [24] = ("NEUR", "High"),
            [25] = ("ORTH", "Medium"),
            [26] = ("TRNS", "Medium"),
            [27] = ("UROL", "Medium"),
            [28] = ("THOR", "Medium"),
            [29] = ("NEUR", "High"),
            [30] = ("SONC", "High"),
            [31] = ("OMF", "High"),
            [32] = ("GENS", "High"),
            [33] = ("UROL", "Medium"),
            [34] = ("OMF", "High"),
            [35] = ("UROL", "Medium"),
            [36] = ("SONC", "High"),
            [37] = ("GYN", "Low"),
            [38] = ("NEUR", "High"),
            [39] = ("SONC", "High"),
            [40] = ("GYN", "Low"),
            [41] = ("NEUR", "High"),
            [42] = ("GENS", "High"),
            [43] = ("ANES", "Low"),
            [44] = ("PLAS", "Low"),
            [45] = ("ORTH", "Medium"),
            [46] = ("GENS", "High"),
            [47] = ("SONC", "High"),
            [48] = ("GYN", "Low"),
            [49] = ("THOR", "Medium"),
            [50] = ("ORTH", "Medium"),
            [51] = ("ORTH", "Medium"),
            [52] = ("THOR", "Medium"),
            [53] = ("NEUR", "High"),
            [54] = ("ORTH", "Medium"),
            [55] = ("NEUR", "High")
        };

    static readonly int[] simulatedCaseOrder =
    {
        17, 32, 42, 11, 8, 22, 46, 24, 23, 55, 1, 2, 38, 41, 16, 10, 29, 53, 31, 34,
        7, 39, 47, 30, 36, 45, 54, 51, 12, 25, 50, 52, 28, 4, 13, 14, 49, 9, 15, 26,
        20, 35, 27, 19, 33, 18, 43, 37, 3, 21, 48, 5, 40, 44, 6
    };

    static readonly int[] appointmentCaseOrder =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 10, 9, 11, 12, 13, 14, 15, 16, 17, 19, 18, 20, 21, 22, 23,
        24, 25, 26, 27, 29, 30, 28, 32, 31, 34, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44,
        45, 46, 47, 48, 50, 49, 51, 52, 53, 54, 55
    };

    static readonly (int Hour, int Minute)[] appointmentTimeSlots =
    {
        (7, 0), (7, 0), (7, 0), (7, 15), (7, 15), (7, 15), (7, 23), (7, 45), (7, 45),
        (7, 45), (7, 55), (8, 15), (8, 15), (8, 15), (8, 15), (8, 47), (9, 10), (9, 15),
        (9, 15), (9, 17), (9, 27), (9, 45), (10, 4), (10, 7), (10, 15), (10, 15), (10, 16),
        (10, 45), (10, 45), (10, 45), (11, 4), (11, 4), (11, 15), (11, 15), (11, 30),
        (11, 48), (11, 49), (11, 51), (11, 55), (12, 15), (12, 47), (12, 57), (13, 12),
        (13, 15), (13, 28), (13, 45), (13, 47), (13, 50), (14, 0), (14, 0), (14, 16),
        (14, 38), (14, 43), (14, 52), (15, 0)
    };

    static readonly Dictionary<string, (int Low, int High)> mdServiceDurationRanges =
        new Dictionary<string, (int Low, int High)>
        {
            ["ANES"] = (27, 31),
            ["GENS"] = (45, 52),
            ["GYN"] = (28, 32),
            ["NEUR"] = (41, 47),
            ["OMF"] = (41, 47),
            ["ORTH"] = (38, 44),
            ["PLAS"] = (15, 20),
            ["SONC"] = (49, 70),
            ["THOR"] = (32, 37),
            ["TRNS"] = (38, 44),
            ["UROL"] = (38, 44)
        };

    static void Main()
    {
        LoadDotEnv();
        dbConnectionString = GetRequiredEnvironmentVariable(DbConnectionEnvVar);
        InitializeStages();

        scheduled_patient = BuildScheduledPatients(simulatedCase > 0 ? simulatedCaseOrder : appointmentCaseOrder);

        // Print all entries
        foreach (var service in scheduled_patient)
        {
            Console.WriteLine($"Patient #{service.PatientNumber} - Service: {service.Service}, Acuity: {service.Acuity}");
        }

        for (int runNumber = 1; runNumber <= 1; runNumber++)
        {
            RunSimulation(runNumber, globalRandom);
        }
    }

    static void LoadDotEnv()
    {
        string? dotEnvPath = FindDotEnvPath();
        if (string.IsNullOrWhiteSpace(dotEnvPath))
        {
            return;
        }

        foreach (string rawLine in System.IO.File.ReadAllLines(dotEnvPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line.Substring("export ".Length).Trim();
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = line.Substring(0, equalsIndex).Trim();
            string value = line.Substring(equalsIndex + 1).Trim();
            if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(1, value.Length - 2);
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    static string? FindDotEnvPath()
    {
        System.IO.DirectoryInfo? current = new System.IO.DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            string candidate = System.IO.Path.Combine(current.FullName, ".env");
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    static string GetRequiredEnvironmentVariable(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Missing required environment variable '{key}'. Create a .env file in the solution root or set it in your shell.");
    }

    static void InitializeStages()
    {
        attendStage = new StageState(
            "Attendant",
            waitRoomQueue,
            waitRoomQueue_Vitals,
            maxOnLunch: 1,
            lunchQueueThreshold: 2,
            usesExamRooms: false,
            requireEndTimeBeforeStart: false,
            usesNursePaperworkCompletion: false,
            computeAddedMinutes: AddAttendantMinutes);

        vitalsStage = new StageState(
            "Vitals",
            waitRoomQueue_Vitals,
            waitRoomQueue_Nurse,
            maxOnLunch: 1,
            lunchQueueThreshold: 2,
            usesExamRooms: false,
            requireEndTimeBeforeStart: false,
            usesNursePaperworkCompletion: false,
            computeAddedMinutes: AddVitalsMinutes);

        rnStage = new StageState(
            "RN",
            waitRoomQueue_Nurse,
            waitRoomQueue_MD,
            maxOnLunch: 2,
            lunchQueueThreshold: 2,
            usesExamRooms: true,
            requireEndTimeBeforeStart: true,
            usesNursePaperworkCompletion: true,
            computeAddedMinutes: AddRnMinutes);

        mdStage = new StageState(
            "MD",
            waitRoomQueue_MD,
            waitRoomQueue_Blood,
            maxOnLunch: 3,
            lunchQueueThreshold: 2,
            usesExamRooms: true,
            requireEndTimeBeforeStart: false,
            usesNursePaperworkCompletion: false,
            computeAddedMinutes: AddMdMinutes);

        bloodStage = new StageState(
            "Blood",
            waitRoomQueue_Blood,
            waitRoomQueue_checkOut,
            maxOnLunch: 1,
            lunchQueueThreshold: 2,
            usesExamRooms: false,
            requireEndTimeBeforeStart: false,
            usesNursePaperworkCompletion: false,
            computeAddedMinutes: AddBloodMinutes);

        AddWorkers(attendStage, "Attendant", attendActive);
        AddWorkers(vitalsStage, "Vitals", vitalsActive);
        AddWorkers(rnStage, "RN", rnActive);
        AddWorkers(mdStage, "MD", mdActive);
        AddWorkers(bloodStage, "Blood", bloodActive);

        allStages = new List<StageState> { attendStage, vitalsStage, rnStage, mdStage, bloodStage };
    }

    static void AddWorkers(StageState stage, string workerPrefix, int[] activeFlags)
    {
        for (int idx = 0; idx < activeFlags.Length; idx++)
        {
            stage.Workers.Add(new WorkerState($"{workerPrefix}{idx + 1}", activeFlags[idx], startTime));
        }
    }

    static List<Patient> BuildScheduledPatients(IEnumerable<int> patientOrder)
    {
        var result = new List<Patient>();

        foreach (int patientNumber in patientOrder)
        {
            if (!patientCatalog.TryGetValue(patientNumber, out var info))
            {
                continue;
            }

            result.Add(new Patient(patientNumber, info.Service, info.Acuity));
        }

        return result;
    }

    static Dictionary<DateTime, int> BuildAppointmentTimes()
    {
        var appointmentTimes = new Dictionary<DateTime, int>();

        foreach (var slot in appointmentTimeSlots)
        {
            DateTime appointmentTime = new DateTime(
                DateTime.Now.Year,
                DateTime.Now.Month,
                DateTime.Now.Day,
                slot.Hour,
                slot.Minute,
                0);

            if (appointmentTimes.ContainsKey(appointmentTime))
            {
                appointmentTimes[appointmentTime] += 1;
            }
            else
            {
                appointmentTimes.Add(appointmentTime, 1);
            }
        }

        return appointmentTimes;
    }

    static void ResetSimulationState()
    {
        examRooms_Occ = 0;
        currentTime = startTime;

        waitRoomQueue.Clear();
        waitRoomQueue_Vitals.Clear();
        waitRoomQueue_Nurse.Clear();
        waitRoomQueue_MD.Clear();
        waitRoomQueue_Blood.Clear();
        waitRoomQueue_checkOut.Clear();

        foreach (StageState stage in allStages)
        {
            foreach (WorkerState worker in stage.Workers)
            {
                worker.Queue.Clear();
                worker.EndTime = currentTime;
                worker.OnLunch = 0;
                worker.HadLunch = 0;
            }
        }
    }

    static void RunSimulation(int recordNumber, Random rnd)
    {
        ResetSimulationState();

        TimeSpan increment = TimeSpan.FromMinutes(1);
        int currentPatientIndex = 0;
        int waitTime = 0;
        Dictionary<DateTime, int> appointmentTimes = BuildAppointmentTimes();

        while (currentTime <= endTime)
        {
            EnqueueNewArrivals(currentPatientIndex, waitTime, appointmentTimes, out currentPatientIndex);

            ProcessStage(
                attendStage,
                rnd,
                worker => currentTime >= worker.EndTime);

            ProcessStage(
                vitalsStage,
                rnd,
                worker => currentTime >= worker.EndTime);

            ProcessStage(
                rnStage,
                rnd,
                worker => ignoreNursePPwrk > 0
                    ? currentTime >= worker.EndTime.AddMinutes(-nursePPwrk)
                    : currentTime >= worker.EndTime);

            ProcessStage(
                mdStage,
                rnd,
                worker => currentTime >= worker.EndTime);

            ProcessStage(
                bloodStage,
                rnd,
                worker => currentTime >= worker.EndTime);

            // Increment currentTime by the defined increment at the end of each loop iteration
            currentTime = currentTime.Add(increment);
        }

        // Output queued patient
        foreach (var patient in waitRoomQueue_checkOut)
        {
            Console.WriteLine($"Patient Number: {patient.PatientNumber}, Arrival Time: {patient.Arrival}, Wait Time: {patient.LOS} minutes");
        }

        PersistPatientRecords(recordNumber);
    }

    static void EnqueueNewArrivals(
        int currentPatientIndex,
        int waitTime,
        Dictionary<DateTime, int> appointmentTimes,
        out int nextPatientIndex)
    {
        nextPatientIndex = currentPatientIndex;

        // Check if the current time matches the queue interval
        if (simulatedCase > 0)
        {
            if (((currentTime.Hour * 60 + currentTime.Minute) % arrivalSpan) == 0)
            {
                if (nextPatientIndex < scheduled_patient.Count)
                {
                    var patient = scheduled_patient[nextPatientIndex];
                    QueuePatientWaitRoom1(patient.PatientNumber, currentTime, waitTime);
                    nextPatientIndex++; // Move to the next patient
                }
            }

            return;
        }

        // Check if the current time matches any of the appointment times
        if (appointmentTimes.TryGetValue(currentTime, out int patientCount))
        {
            while (patientCount > 0 && nextPatientIndex < scheduled_patient.Count)
            {
                var patient = scheduled_patient[nextPatientIndex];
                QueuePatientWaitRoom1(patient.PatientNumber, currentTime, waitTime);
                nextPatientIndex++; // Move to the next patient
                patientCount--; // Decrease count for the current time slot
            }
        }
    }

    // WAITROOM QUEUE
    static void QueuePatientWaitRoom1(int patientNumber, DateTime arrivalTime, int waitTime)
    {
        var patient = scheduled_patient.FirstOrDefault(p => p.PatientNumber == patientNumber);

        if (patient != null)
        {
            patient.Arrival = arrivalTime;
            patient.LOS = waitTime;
            waitRoomQueue.Enqueue(patient);
        }
        else
        {
            Console.WriteLine("Patient not found.");
        }
    }

    static void ProcessStage(StageState stage, Random rnd, Func<WorkerState, bool> canDispatch)
    {
        foreach (WorkerState worker in stage.Workers)
        {
            if (worker.Active <= 0)
            {
                continue;
            }

            if (stage.InputQueue.Count <= 0 && worker.Queue.Count <= 0)
            {
                continue;
            }

            if (!canDispatch(worker))
            {
                continue;
            }

            ProcessWorker(stage, worker, rnd);
        }
    }

    static void ProcessWorker(StageState stage, WorkerState worker, Random rnd)
    {
        CompleteCurrentPatientIfReady(stage, worker);
        UpdateLunchState(stage, worker);

        bool canStartNewPatient =
            worker.Queue.Count < stage.CapacityPerWorker
            && stage.InputQueue.Count > 0
            && (!stage.UsesExamRooms || examRooms_Occ < examRooms)
            && (!stage.RequireEndTimeBeforeStart || currentTime >= worker.EndTime);

        if (canStartNewPatient)
        {
            if (stage.UsesExamRooms)
            {
                examRooms_Occ++;
            }

            var patient = stage.InputQueue.Dequeue();
            worker.Queue.Enqueue(patient);

            Console.WriteLine(
                $"{worker.Name} START| Patient Number| {patient.PatientNumber}| IN| {currentTime}| Visit Time| {patient.LOS} minutes");

            if (patient.Arrival.HasValue)
            {
                TimeSpan duration = currentTime - patient.Arrival.Value;
                if ((int)duration.TotalMinutes > patient.LOS)
                {
                    patient.LOS = (int)duration.TotalMinutes;
                }

                int? addedMinutes = stage.ComputeAddedMinutes(patient, rnd);
                if (!addedMinutes.HasValue)
                {
                    return;
                }

                patient.LOS = patient.LOS + addedMinutes.Value;
            }

            if (patient.Arrival.HasValue && patient.LOS.HasValue)
            {
                if (worker.OnLunch > 0)
                {
                    worker.EndTime = patient.Arrival.Value
                        .AddMinutes(patient.LOS.Value)
                        .AddMinutes(lunchDuration);
                    Console.WriteLine($"{worker.Name} LUNCH BREAK| {currentTime}");
                }
                else
                {
                    worker.EndTime = patient.Arrival.Value.AddMinutes(patient.LOS.Value);
                }
            }
        }
        else if (worker.OnLunch > 0)
        {
            worker.EndTime = currentTime.AddMinutes(lunchDuration);
            Console.WriteLine($"{worker.Name} LUNCH BREAK| {currentTime}");
        }
    }

    static void CompleteCurrentPatientIfReady(StageState stage, WorkerState worker)
    {
        if (worker.Queue.Count <= 0)
        {
            return;
        }

        var firstPatient = worker.Queue.Peek();
        bool isComplete = false;
        bool hideCompletionLog = false;

        if (stage.UsesNursePaperworkCompletion && ignoreNursePPwrk > 0)
        {
            isComplete = currentTime >= worker.EndTime.AddMinutes(-RnPaperworkMinutes);
            hideCompletionLog = true;
        }
        else if (firstPatient.Arrival.HasValue && firstPatient.LOS.HasValue)
        {
            isComplete = currentTime >= firstPatient.Arrival.Value.AddMinutes(firstPatient.LOS.Value);
        }

        if (!isComplete)
        {
            return;
        }

        stage.OutputQueue.Enqueue(firstPatient);
        worker.Queue.Dequeue();

        if (stage.UsesExamRooms)
        {
            examRooms_Occ--;
        }

        if (!hideCompletionLog)
        {
            Console.WriteLine(
                $"{worker.Name} END| Patient Number| {firstPatient.PatientNumber}| OUT| {currentTime}| Visit Time| {firstPatient.LOS} minutes");
        }
    }

    static void UpdateLunchState(StageState stage, WorkerState worker)
    {
        if (currentTime >= lunchStartTime
            && currentTime <= lunchEndTime
            && stage.InputQueue.Count <= stage.LunchQueueThreshold
            && worker.HadLunch == 0
            && GetTotalWorkersOnLunch(stage) < stage.MaxOnLunch)
        {
            worker.OnLunch++;
            worker.HadLunch++;
        }
        else if (worker.OnLunch > 0)
        {
            worker.OnLunch--;
        }
    }

    static int GetTotalWorkersOnLunch(StageState stage)
    {
        return stage.Workers.Sum(worker => worker.OnLunch);
    }

    static int? AddAttendantMinutes(Patient patient, Random rnd)
    {
        return 7;
    }

    static int? AddVitalsMinutes(Patient patient, Random rnd)
    {
        return 10;
    }

    static int? AddBloodMinutes(Patient patient, Random rnd)
    {
        return 7;
    }

    static int? AddRnMinutes(Patient patient, Random rnd)
    {
        int avgChart = 5;
        int highChart = 20;
        int assess = 27;
        int ppWrk = 11;

        int randomValue = rnd.Next(1, 10);
        int prob = 0; // effective probability

        if (patient.Acuity == "High" && randomValue <= prob)
        {
            avgChart = highChart;
        }

        return avgChart + assess + ppWrk;
    }

    static int? AddMdMinutes(Patient patient, Random rnd)
    {
        if (!mdServiceDurationRanges.TryGetValue(patient.Service, out var range))
        {
            // Handle unknown service
            return null;
        }

        return rnd.Next(range.Low, range.High + 1);
    }

    static void PersistPatientRecords(int recordNumber)
    {
        foreach (var patient in waitRoomQueue_checkOut)
        {
            // Calculate Departure assuming Arrival is DateTime and LOS is in minutes
            var departure = patient.Arrival.HasValue && patient.LOS.HasValue
                ? patient.Arrival.Value.AddMinutes(patient.LOS.Value)
                : (DateTime?)null;

            string insertQuery = @"
            INSERT INTO [Tinuum_Software].[dbo].[PatientRecords]
            ([RecordNumber], [PatientNumber], [Service], [Acuity], [Arrival], [LOS], [Departure])
            VALUES
            (@RecordNumber, @PatientNumber, @Service, @Acuity, @Arrival, @LOS, @Departure)";

            using (SqlConnection connection = new SqlConnection(dbConnectionString))
            using (SqlCommand command = new SqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@RecordNumber", recordNumber);
                command.Parameters.AddWithValue("@PatientNumber", patient.PatientNumber);
                command.Parameters.AddWithValue("@Service", patient.Service);
                command.Parameters.AddWithValue("@Acuity", patient.Acuity);
                command.Parameters.AddWithValue("@Arrival", patient.Arrival ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@LOS", patient.LOS ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Departure", departure ?? (object)DBNull.Value);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }
    }
}
