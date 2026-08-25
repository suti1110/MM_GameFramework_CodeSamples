using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;

public class VehicleControl : MonoBehaviour, ICarInfo
{
    /// <summary>
    /// 현재 속도 (km/h)
    /// </summary>
    public float CurSpeed
    {
        get
        {
            return Rb.linearVelocity.magnitude * 3.6f;
        }
    }

    /// <summary>
    /// 현재 바퀴 RPM
    /// </summary>
    public float RPM
    {
        get
        {
            float sum = 0;
            int count = 0;

            foreach (Wheel wheel in wheels)
            {
                sum += wheel.wheelCollider.rpm;
                count++;
            }

            return Mathf.Abs(sum / count);
        }
    }

    /// <summary>
    /// 현재 바퀴 모터 토크
    /// </summary>
    public float WheelMotorTorque
    {
        get
        {
            float sum = 0;
            int count = 0;
            foreach (Wheel wheel in wheels)
            {
                sum += wheel.wheelCollider.motorTorque;
                count++;
            }
            return sum / count;
        }
        set
        {
            foreach (Wheel wheel in wheels)
            {
                wheel.wheelCollider.motorTorque = value;
            }
        }
    }

    /// <summary>
    /// 현재 바퀴 브레이크 토크
    /// </summary>
    public float WheelBrakeTorque
    {
        get
        {
            float sum = 0;
            int count = 0;
            foreach (Wheel wheel in wheels)
            {
                sum += wheel.wheelCollider.brakeTorque;
                count++;
            }
            return sum / count;
        }
        set
        {
            foreach (Wheel wheel in wheels)
            {
                wheel.wheelCollider.brakeTorque = value;
            }
        }
    }

    public VehicleStat stat;
    public Rigidbody Rb { get; private set; }

    [System.Serializable, Tooltip("반드시 FrontLeft, FrontRight, RearLeft, RearRight순으로 넣기.")]
    public struct Wheel
    {
        [Tooltip("바퀴의 WheelCollider")]
        public WheelCollider wheelCollider;

        [Tooltip("바퀴의 중심 오브젝트")]
        public Transform wheelObject;

        [Tooltip("조향 가능한 바퀴인지 여부")]
        public bool isSteering;
    }

    [Tooltip("자동차의 바퀴")]
    public Wheel[] wheels;

    public enum Gear
    {
        D,
        R,
        P,
        N
    }

    public enum CarState
    {
        Stopped,
        Idle,
        Driving,
        Converting
    }

    [Range(-1, 1)] public int carDirection = 1; // 1: 전진, -1: 후진

    [SerializeField] private float curSpeed;
    [SerializeField] private int currentGear;
    [SerializeField] private float curRPM;
    [SerializeField] private float gearRatio;
    [SerializeField] private float torque;

    public int CurrentGear => currentGear;
    public float CurRPM => curRPM;
    public float GearRatio => gearRatio;
    public float Torque => torque;

    private ICarControl carControl;

    private IScore score;

    public Func<float, bool> SpeedLimit { private get; set; } = (speed) => false;

    [SerializeField] private GameObject backCamera;

    private void Awake()
    {
        stat.Vehicle = this;
        Rb = GetComponent<Rigidbody>();
        score = GetComponent<IScore>();
        carControl = GetComponent<ICarControl>();
    }

    private void FixedUpdate()
    {
        UpdateEngineRPM();

        switch (carControl.Gear)
        {
            case Gear.D:
                AutoConvertGear();
                ApplyEngineRPM();
                ApplyBrakes();
                break;
            case Gear.R:
                ApplyEngineRPM();
                ApplyBrakes();
                break;
            case Gear.P:
                LockGear();
                break;
            case Gear.N:
                ApplyBrakes();
                break;
        }

        ApplyResistanceForces();
        ApplyAntiRollBar();
    }

    private void Update()
    {
        UpdateWheels();

        curSpeed = CurSpeed;

        if (SpeedLimit(curSpeed))
        {
            score.Score -= 100 * Time.deltaTime;
        }

        carControl.VehicleLight.DirectionLightScore(Rb, score);
    }

    public void UpdateEngineRPM()
    {
        if (carControl.State == CarState.Stopped)
        {
            curRPM = Mathf.Lerp(curRPM, 0f, Time.deltaTime * 2f);
            return;
        }

        if (carControl.State == CarState.Converting)
        {
            curRPM = Mathf.Lerp(curRPM, stat.idleRPM, Time.deltaTime * 2f);
            return;
        }

        if (carControl.Gear == Gear.D || carControl.Gear == Gear.R)
        {
            gearRatio = carControl.Gear == Gear.D ? stat.gearRatios[currentGear] : stat.reverseGearRatio;

            float engineRPMBasedWheel = Mathf.Min(stat.redLine, RPM * gearRatio * stat.finalDrive);

            curRPM = Mathf.Lerp(curRPM, Mathf.Max(stat.idleRPM, engineRPMBasedWheel), Time.deltaTime * 5f); // 필요 시 Lerp추가 예정
        }
        else
        {
            if (carControl.AcceleratorDepth > 0.05f)
            {
                curRPM = Mathf.Lerp(curRPM, stat.redLine, Time.deltaTime * 5f * carControl.AcceleratorDepth);
            }
            else
            {
                curRPM = Mathf.Lerp(curRPM, stat.idleRPM, Time.deltaTime * 2f);
            }
        }
    }

    // 엔진RPM을 적용하는 부분
    public void ApplyEngineRPM()
    {
        if (carControl.State != CarState.Driving) return;

        torque = 0;

        if (carControl.AcceleratorDepth > 0)
        {
            torque = stat.engineRPMToHp.Evaluate(curRPM / stat.redLine) * stat.maxEnginePower / curRPM * 7023.5f * gearRatio * stat.finalDrive * 0.9f * carControl.AcceleratorDepth;
        }
        else
        {
            torque = -stat.engineBrakeStrength * Mathf.InverseLerp(stat.idleRPM, stat.redLine, curRPM) * gearRatio;
        }

        torque *= carControl.Gear == Gear.D ? 1 : carControl.Gear == Gear.R ? -1 : 0;
        torque *= carDirection;

        int length = wheels.Length;
        torque /= length;

        for (int i = 0; i < length; i += 2)
        {
            ApplyLSDPair(wheels[i].wheelCollider, wheels[i + 1].wheelCollider, torque * 2);
        }
    }

    private void ApplyLSDPair(WheelCollider left, WheelCollider right, float totalTorque)
    {
        bool leftGrounded = left.isGrounded;
        bool rightGrounded = right.isGrounded;

        // 둘 다 공중에 뜨면 균등 분배
        if (!leftGrounded && !rightGrounded)
        {
            left.motorTorque = totalTorque / 2f;
            right.motorTorque = totalTorque / 2f;
            return;
        }

        // 한쪽만 공중에 뜰 때 (0이면 50:50 균등 분배, 1이면 접지된 쪽에 100% 몰빵)
        if (!leftGrounded)
        {
            left.motorTorque = totalTorque * (0.5f - 0.5f * stat.lsdStrength);
            right.motorTorque = totalTorque * (0.5f + 0.5f * stat.lsdStrength);
            return;
        }

        if (!rightGrounded)
        {
            left.motorTorque = totalTorque * (0.5f + 0.5f * stat.lsdStrength);
            right.motorTorque = totalTorque * (0.5f - 0.5f * stat.lsdStrength);
            return;
        }

        // 둘 다 접지된 경우: RPM 차이 기반 LSD
        float leftRPM = Mathf.Abs(left.rpm);
        float rightRPM = Mathf.Abs(right.rpm);
        float rpmDifference = Mathf.Abs(leftRPM - rightRPM);

        if (rpmDifference < stat.lsdActivationThreshold)
        {
            left.motorTorque = totalTorque / 2f;
            right.motorTorque = totalTorque / 2f;
        }
        else
        {
            float avgRPM = (leftRPM + rightRPM) / 2f;
            float slipRatio = avgRPM > 1f ? Mathf.Clamp01(rpmDifference / avgRPM) : 0f;

            // 0이면 0.5(균등), 1이면 최대 1.0(몰빵)이 되도록 수식 수정
            float lsdEffect = Mathf.Lerp(0.5f, 0.5f + (stat.lsdStrength * 0.5f), slipRatio);

            if (leftRPM < rightRPM)
            {
                left.motorTorque = totalTorque * lsdEffect;
                right.motorTorque = totalTorque * (1f - lsdEffect);
            }
            else
            {
                left.motorTorque = totalTorque * (1f - lsdEffect);
                right.motorTorque = totalTorque * lsdEffect;
            }
        }
    }

    private void ApplyResistanceForces()
    {
        Vector3 velocity = Rb.linearVelocity;

        float speed = velocity.magnitude;
        float F_drag = 0.5f * Environment.AirDensity(transform.position.y) * stat.dragCoefficient * stat.frontArea * speed * speed;
        float F_roll = (stat.rollingResistanceCoeff + 0.002f * speed) * Rb.mass * Physics.gravity.magnitude;

        float F_total = F_drag + F_roll;
        Vector3 resistanceForce = -velocity.normalized * F_total;

        Rb.AddForce(resistanceForce);
    }

    private bool preFrameBrakeLight = false;

    void ApplyBrakes()
    {
        float pistonForce = carControl.BrakeDepth * stat.maxBrakePressure;
        float brakeTorque = stat.brakePadFriction * pistonForce * stat.discRadius;
        float frontTorque = brakeTorque * stat.frontBrakeBias;
        float rearTorque = brakeTorque * (1f - stat.frontBrakeBias);

        foreach (Wheel wheel in wheels)
        {
            float torque = wheel.isSteering ? frontTorque : rearTorque;

            ApplyBrakeToWheel(wheel.wheelCollider, torque);
        }

        if (WheelBrakeTorque >= 100)
        {
            if (preFrameBrakeLight) return;

            carControl.VehicleLight.LightControl(true);
            
            preFrameBrakeLight = true;

            WaitAction.Wait(2f, () =>
            {
                carControl.VehicleLight.LightControl(false);
            });
        }
        else preFrameBrakeLight = false;
    }

    void ApplyBrakeToWheel(WheelCollider wheel, float torque)
    {
        if (wheel.GetGroundHit(out WheelHit hit))
        {
            // Slip 기반 ABS 제어
            if (Mathf.Abs(hit.forwardSlip) > stat.absSlipThreshold)
            {
                torque *= 0.3f; // 압력 순간 감소(ABS 펌핑 효과)
            }
        }

        wheel.brakeTorque = torque;
    }

    void ApplyAntiRollBar()
    {
        ApplyAntiRollPair(wheels[0].wheelCollider, wheels[1].wheelCollider, stat.antiRollStiffnessFront);
        ApplyAntiRollPair(wheels[2].wheelCollider, wheels[3].wheelCollider, stat.antiRollStiffnessRear);
    }

    void ApplyAntiRollPair(WheelCollider left, WheelCollider right, float antiRoll)
    {
        float travelL = 1f;
        float travelR = 1f;

        bool groundL = left.GetGroundHit(out WheelHit hit);
        if (groundL)
        {
            float wheelLocalY = left.transform.InverseTransformPoint(hit.point).y;
            travelL = Mathf.Clamp01((-wheelLocalY - left.radius) / left.suspensionDistance);
        }

        bool groundR = right.GetGroundHit(out hit);
        if (groundR)
        {
            float wheelLocalY = right.transform.InverseTransformPoint(hit.point).y;
            travelR = Mathf.Clamp01((-wheelLocalY - right.radius) / right.suspensionDistance);
        }

        float antiRollForce = (travelL - travelR) * antiRoll;

        if (groundL) Rb.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
        if (groundR) Rb.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
    }

    // 조향각을 관장하는 부분
    public void UpdateWheels()
    {
        foreach (Wheel wheel in wheels)
        {
            if (wheel.isSteering) wheel.wheelCollider.steerAngle = stat.steerAngle.Evaluate(Mathf.Min(CurSpeed / stat.maxKiloMeterPerHour, 1)) * carControl.HandleAngle * stat.maxSteerAngle;
            wheel.wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion temp);

            wheel.wheelObject.position = pos;
            wheel.wheelObject.rotation = temp;
        }
    }

    public void ManualGear(Gear gear)
    {
        if (CurSpeed >= 1)
        {
            MassageManager.Log("자동차가 이동 중에는 기어를 변경할 수 없습니다!!!");
            return;
        }

        if (gear == Gear.P && !isPendingParking)
        {
            MassageManager.Log("주차 가능 구역이 아닙니다.");
            return;
        }

        carControl.Gear = gear;
        currentGear = 0;

        WheelBrakeTorque = 0;

        isLockGear = false;

        switch (gear)
        {
            case Gear.D:
                carControl.State = CarState.Driving;
                if (backCamera) backCamera.SetActive(false);
                break;
            case Gear.R:
                carControl.State = CarState.Driving;
                if (backCamera) backCamera.SetActive(true);
                break;
            case Gear.P:
                carControl.State = CarState.Idle;
                if (backCamera) backCamera.SetActive(false);
                SetParking();
                break;
            case Gear.N:
                carControl.State = CarState.Idle;
                if (backCamera) backCamera.SetActive(false);
                break;
        }
    }

    private bool isLockGear = false;

    private void SetParking()
    {
        Vector3 position = transform.position;

        WaitAction.WaitUntil(() => Vector3.Distance(position, transform.position) >= 0.05f, () =>
        {
            isLockGear = true;

            Rb.AddForce(-Rb.linearVelocity * Rb.mass / 10f, ForceMode.Impulse);
        });

        GameManager.Instance.score = GetComponent<IScore>().Score;
        SceneChanger.FadeIn("ResultScene");
    }

    private void LockGear()
    {
        if (isLockGear)
        {
            WheelBrakeTorque = Mathf.Infinity;
        }
    }

    private bool canAutoConvert = true;

    private void AutoConvertGear()
    {
        if (carControl.State == CarState.Converting && canAutoConvert) return;

        if (curRPM <= stat.decreaseGearRPM && currentGear > 0)
        {
            carControl.State = CarState.Converting;
            canAutoConvert = false;

            WheelMotorTorque = 0;

            WaitAction.Wait(0.2f, () =>
            {
                if (currentGear > 0)
                {
                    currentGear--;
                }

                carControl.State = CarState.Driving;
                WaitAction.Wait(0.2f, () =>
                {
                    canAutoConvert = true;
                });
            });
        }
        else if (curRPM >= stat.increaseGearRPM && currentGear < stat.gearRatios.Length - 1)
        {
            carControl.State = CarState.Converting;
            canAutoConvert = false;

            WheelMotorTorque = 0;

            WaitAction.Wait(0.2f, () =>
            {
                if (currentGear < stat.gearRatios.Length - 1)
                {
                    currentGear++;
                }

                carControl.State = CarState.Driving;
                WaitAction.Wait(0.2f, () =>
                {
                    canAutoConvert = true;
                });
            });
        }
    }

    private bool isPendingParking = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out Parking _))
        {
            isPendingParking = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out Parking _))
        {
            isPendingParking = false;
        }
    }
}
