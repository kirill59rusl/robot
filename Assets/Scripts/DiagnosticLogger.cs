using UnityEngine;
using System.IO;
using System.Text;
using System.Globalization;

public class DiagnosticLogger : MonoBehaviour
{
    public bool enableLogging = true;
    public int logEveryN = 1;
    public int maxRows = 10000; // Увеличим лимит, так как данных будет много

    private StreamWriter writer;
    private int rowsWritten = 0;
    private float startTime;

    void Start()
    {
        if (enableLogging)
        {
            string path = Path.Combine(Application.dataPath, "..", "diagnostic_log.csv");
            writer = new StreamWriter(path, false, Encoding.UTF8);

            // ДОБАВЛЕНЫ: epId, trueDist, stepRew, totalRew
            writer.WriteLine("time,epId,step,ballSeen,ballAngle,perceivedDist,trueDist,uz,irL,irR,gripIR,camYaw,gas,steering,hasBall,holdTicks,isRetrying,displacementX,displacementZ,heading,speed,stepRew,totalRew");

            startTime = Time.time;
            Debug.Log($"[DiagnosticLogger] Запись лога запущена в: {path}");
        }
    }

    public void LogStep(
        int epId, int step, bool ballSeen, float ballAngle, float perceivedDist, float trueDist,
        float uz, float irL, float irR, float gripIR, float camYaw,
        float gas, float steering, bool hasBall, int holdTicks,
        bool isRetrying, float displacementX, float displacementZ,
        float heading, float speed, float stepRew, float totalRew)
    {
        if (!enableLogging || writer == null || rowsWritten >= maxRows) return;
        if (step % logEveryN != 0) return;

        float elapsed = Time.time - startTime;

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0:F3},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14},{15},{16},{17:F4},{18:F4},{19:F4},{20:F4},{21:F4},{22:F4}",
            elapsed, epId, step, ballSeen ? 1 : 0, ballAngle, perceivedDist, trueDist, uz, irL, irR, gripIR, camYaw,
            gas, steering, hasBall ? 1 : 0, holdTicks, isRetrying ? 1 : 0,
            displacementX, displacementZ, heading, speed, stepRew, totalRew);

        writer.WriteLine(line);
        writer.Flush();
        rowsWritten++;
    }

    void OnDestroy()
    {
        writer?.Close();
    }
}