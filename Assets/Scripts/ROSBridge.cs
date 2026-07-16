using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
 
public class RosBridge : MonoBehaviour
{
    [Header("Topics")]
    public string cmdVelTopic = "/cmd_vel";
    public string gripperTopic = "/cmd_gripper";
    public string cameraTopic = "/cmd_camera_pan";
 
    private ROSConnection ros;
 
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
 
        ros.RegisterPublisher<TwistMsg>(cmdVelTopic);
        ros.RegisterPublisher<Int32Msg>(gripperTopic);
        ros.RegisterPublisher<Float32Msg>(cameraTopic);
 
        Debug.Log("ROS TCP Connector ready");
    }
 
    public void SendVelocity(float gas, float steering)
    {
        TwistMsg msg = new TwistMsg();
 
        msg.linear.x = gas;
        msg.linear.y = 0;
        msg.linear.z = 0;
 
        msg.angular.x = 0;
        msg.angular.y = 0;
        msg.angular.z = steering;
 
        ros.Publish(cmdVelTopic, msg);
    }
 
    public void SendGripper(int command)
    {
        ros.Publish(gripperTopic, new Int32Msg(command));
    }
 
    // angleNormalized = -1 ... +1
    public void SendCamera(float angleNormalized)
    {
        ros.Publish(cameraTopic, new Float32Msg(angleNormalized));
    }
 
    private void OnApplicationQuit()
    {
        SendVelocity(0, 0);
    }
}
