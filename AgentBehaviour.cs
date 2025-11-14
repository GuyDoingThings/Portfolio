using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AgentController : MonoBehaviour
{
    #region BaseInstanceCallsReference
    [Header("BaseInstanceCalls")]
    private NavMeshAgent agent;
    [SerializeField] private PlayerController playerControllerScript;
    [SerializeField] private GameObject target;
    [SerializeField] private GameObject targetShell;
    [SerializeField] public Transform targetTransform;
    [SerializeField] private Gun1 gunScript;
    [SerializeField] private PositionCheckerSweepAssistant positionCheckerSweepAssistantScript;

    [SerializeField] private AudioSource agentHitSound;
    [SerializeField] private ExclusiveAudioControllerForStates stateAudioScript;
    [SerializeField] private ExclusiveAudioController randomSoundsScript;
    [SerializeField] private SpeedReader speedReaderScript;

    #region NavigatioManipulation
    #region AvoidanceReference
    [Header("LocalAgentAvoidance")]
    [SerializeField] private float avoidanceOuterRadius = 3f;        // Ab hier beginnt Einfluss (OverlapSphere-Radius)
    [SerializeField] private float avoidanceInnerRadius = 1f;        // Ab diesem Abstand volle Avoidance
    [SerializeField] private float avoidanceSpeedMultiplier = 1.0f;  // Stärke des Weglauf-Vektors relativ zur Agent-Speed
    [SerializeField] private float avoidanceStrength = 1f; // 1 = normal, >1 = stärker, <1 = schwächer
    [SerializeField] private LayerMask agentAvoidanceMask;           // Layer, auf dem deine Agents liegen
    // fester, per-Agent zufälliger Vorrang (0..1)
    private float avoidancePriority;
    public float AvoidancePriority => avoidancePriority;
    [SerializeField] private float sideStepStrength = 0.5f; // 0 = nur weg, 1 = deutlich zur Seite

    [Header("TargetAvoidance")]
    [SerializeField] private float targetAvoidanceOuterRadius = 3f;
    [SerializeField] private float targetAvoidanceInnerRadius = 1f;
    [SerializeField] private float targetAvoidanceStrength = 1f;
    [SerializeField] private float targetAvoidanceSpeedMultiplier = 1f;
    [SerializeField] private float targetSideStepStrength = 0.5f;
    #endregion
    #region NavigationAreas
    public NavArea CurrentNavArea;

    #endregion
    #endregion
    private enum AgentState
    {
        None,
        Dead,
        Stay,
        Guard,
        Patrol,
        SuspectPassively,
        SuspectActively,
        ChaseTarget,
        ApproachTarget,
        AttackTarget
    }

    private enum AgentTask
    {
        None,
        Stay,
        Patrol
    }

    private enum TargetDetectionLevel //xcv private/public im moment verwaltet die gunagent selbst die feuerrate. sehr invasiv wie es scheint.
    {
        None,
        PassiveSuspicion,
        ActiveSuspicion,
        DetectionMemory,
        DetectingTarget
    }

    private enum DetectionManagerLevel
    {
        None,               // No detection, no suspicion
        PassiveSuspicion,    // Weak suspicion (e.g., heard something)
        ActiveSuspicion,    // Strong suspicion but no direct sight
        DetectingTarget,    // Directly sees/feels target
        PassiveSuspicionMemory,  // condition for staying in passive sus
        ActiveSuspicionMemory,   // condition for staying in active sus
        DetectionMemory,    // knowing target position for a short time after losing sight
    }

    public enum SightDetection
    {
        None,
        PassiveSuspicion,
        ActiveSuspicion,
        DetectingTarget
    }

    private enum FeltDetection
    {
        None,
        PassiveSuspicion,
        ActiveSuspicion,
        DetectingTarget
    }

    private enum HeardDetection
    {
        None,
        PassiveSuspicion,
        ActiveSuspicion,
        DetectingTarget
    }

    private enum Need
    {
        None, 
        SatisfyPassiveSuspicion,
        SatisfyActiveSuspicion,
        ReachTarget,
        AttackTarget,
    }

    #endregion

    #region StateManagementReference
    [Header("StateManagement")]
    private AgentState agentState;
    [SerializeField] private AgentState initialAgentState;
    [SerializeField] private AgentState previousAgentState; // Stores last state
    [SerializeField] private AgentTask agentTask;
    [SerializeField] private Need agentNeed;
    [SerializeField] private Need previousAgentNeed;
    [SerializeField] private float effectiveRange = 10f;
    [SerializeField] public bool targetInEffectiveRange;
    #endregion

    #region BodyPartsReference / RotationReference / RotatorReference
    [SerializeField] private GameObject head;
    [SerializeField] private GameObject torso;
    [SerializeField] private GameObject arms;
    [SerializeField] private GameObject legs;


    #region BodyPartsReference
    private Quaternion originalLocalTorsoRotation;
    [SerializeField] private float maxTorsoRotationAngle = 60f;

    #endregion
    #endregion

    #region DetectionManagementReference
    [Header("DetectionManagement")]
    private TargetDetectionLevel targetDetectionLevel = TargetDetectionLevel.None;
    private DetectionManagerLevel detectionManagerLevel = DetectionManagerLevel.None;
    public SightDetection sightDetection = SightDetection.None;
    private FeltDetection feltDetection = FeltDetection.None;
    private HeardDetection heardDetection = HeardDetection.None;
    private float distanceToTarget;

    private float detectionHoldTimer = 0f;
    [SerializeField] private float detectionHoldDuration = 2f; // 2-second memory
    
    // Timer for detection memory
    private float detectionMemoryTimer = 0f;
    [SerializeField] private float detectionMemoryDuration = 10f; // 2-second memory

    // condition for activesuspicion // a timer for the moment / xcv
    private float activeSuspicionMemoryTimer = 0f;
    [SerializeField] private float activeSuspicionMemoryDuration = 200f; // 2-second memory

    // condition for passivesuspciion //  a timer for the moment //xcv
    private float passiveSuspicionMemoryTimer = 0f;
    [SerializeField] private float passiveSuspicionMemoryDuration = 200f; // 2-second memory
    #endregion

    #region HealthReference
    [Header("Health")]
    [SerializeField] private float agentMaxHealth = 100f;
    [SerializeField] public float agentHealth = 100f;
    [SerializeField] private float damagePerParticle = 10f;
    #endregion

    #region PatrolStateReference
    [Header("PatrolState")]
    [SerializeField] private float stoppingDistancePatrol = 1.2f;
    [SerializeField] private List<Transform> patrolPoints;
    private int currentPatrolIndex = 0;
    #endregion

    #region StayStateReference
    [Header("StayState")]
    [SerializeField] private float stoppingDistanceStay = 0.1f;
    private Vector3 agentStayPosition;
    #endregion

    #region FollowTargetStateReference
    [Header("FollowTargetState")]
    [SerializeField] private float stoppingDistanceFollowTarget = 2f;
    #endregion

    #region DetectionSightReference
    [Header("TargetDetectionSight")]
    [SerializeField] private SightDetectionClose sightDetectionCloseScript;
    [SerializeField] private SightDetectionCenter sightDetectionCenterScript;
    [SerializeField] private SightDetectionPeripheral sightDetectionPeripheralScript;
    [SerializeField] private Transform sightSourceTransform;
                     private float agentTargetSightMeter = 0f;
                     private float agentTargetMaxSightMeter = 1f;
    [SerializeField] private float maxDetectionSightDistance = 10f;
    [SerializeField] private float sightDetCloseBaseRate = 0.1f;
    [SerializeField] private float sightDetCenterBaseRate = 0.02f;
    [SerializeField] private float sightDetPeriphBaseRate = 0.01f;
    [SerializeField] private float sightRayDetectionDecayRate = 0.1f;
    [SerializeField] private float sightDistanceDetectionDecayRate = 0.1f;
    [SerializeField] private float sightColliderDetectionDecayRate = 0.1f;
    [SerializeField] private float agentTargetSightActiveSuspicionTreshhold = 0.666f;
    [SerializeField] private float agentTargetSightPassiveSuspicionTreshhold = 0.333f;
    private int layerMask = (1 << 0 | 1 << 7 | 1 << 8);
    #endregion

    #region DetectionFeltReference
    [Header("TargetDetectionFelt")]
    [SerializeField] private FeltDetectionClose feltDetectionCloseScript;
    [SerializeField] private float maxDetectionFeltDistance = 2f;
    [SerializeField] private float feltDetCloseBaseRate = 0.2f;
                     private float agentTargetFeltMeter = 0f;
                     private float agentTargetMaxFeltMeter = 1f;
    [SerializeField] private float feltColliderDetectionDecayRate = 0.1f;
    [SerializeField] private float agentTargetFeltActiveSuspicionTreshhold = 0.666f;
    [SerializeField] private float agentTargetFeltPassiveSuspicionTreshhold = 0.333f;
    #endregion

    #region DetectionHeardReference
    [SerializeField] private SoundManager soundManager;
    [SerializeField] private float agentTargetMaxHeardMeter = 1f;
    [SerializeField] private float agentTargetHeardMeter = 0f;
    [SerializeField] private float heardDetBaseRate = 1f;
    [SerializeField] private float heardDetectionDecayRate = 0.1f;
    private float heardSoundintensity = 0f;
    #endregion

    #region TargetSuspicionReference
    private Vector3 suspicionPosition;
    private Vector3 agentReturnPosition;
    #endregion

    #region AgentDirectionRotationReference
    [Header("AgentRotations")]
    private Vector3 agentDirectionVector;
    private Vector3 lastAgentDirectionVector;
    private Quaternion originalLocalHeadRotation; // Store the initial local rotation
    [SerializeField] private float maxHeadRotationAngle = 60f;
    [SerializeField] private float requiredStableTime = 0.2f; // Time the new direction must stay stable
    private float angleCheckStartTime = 0f;  // When the new angle was first detected
    private Quaternion lastStableLocalRotation; // Stores last stable local rotation
    #endregion

    #region ArmsReference
    [SerializeField] private float maxarmsRotationAngle;
    private Quaternion originalLocalArmAndGunRotation;
    #endregion

    #region AttackReference
    [SerializeField] private float shotRange = 30f;
    [SerializeField] public bool targetInShotRange = false;
    #endregion

    #region ActiveSuspicionReference
    private bool suspicionPositionReached = true;
    private float satisfactionDistance = 1f;
    private float returnDistance = 5f;
    [SerializeField] private float headSearchPitchAngle = -10f;              // fester Blickwinkel nach oben/unten (in Grad)
    [SerializeField] private float headSearchYawSpeedDegPerSec = 45f;        // Rotationsgeschwindigkeit um die Y-Achse (in Grad/Sek)
    private float currentSearchHeadYawAngle = 0f;                            // interner Yaw-Offset

    #endregion

    #region UniversalBehaviourAndActionMethdodReference
    private Vector3 lastDestination;
    private bool isCalculatingPath = false; // Prevent multiple coroutine instances
    #endregion

    #region AnimationReference
    [SerializeField] private Animator legsAnimator;
    [SerializeField] private float agentAnimWalkSpeedFactor;
    #endregion

    #region DeathReference
    [SerializeField] private float deathCleanupDelay = 2.5f; // Time to wait before deactivation
    [SerializeField] private AudioSource deathSound;

    [SerializeField] private GameObject physicsColliders;
    [SerializeField] private GameObject abstractColliders;
    [SerializeField] private GameObject partsAndColliders;

    [SerializeField] private ParticleSystem DeathParticles;

    private bool isDead = false;

    #endregion

    #region CalloutReference
    [SerializeField] private float interval = 1.5f;
    private float timer;
    #endregion


    #region UnityRuntimeMethdods //................................Methods after this
    private void Awake()
    {
        #region ObjectCollection
        agent = GetComponent<NavMeshAgent>();
        #endregion

        #region NavMeshAgentProperties     
        agent.updatePosition = true;
        agent.updateRotation = true; // agent will rotate wrong if not turned false
        #endregion

        #region StateHandling
        agentState = initialAgentState;
        #endregion

        #region InitializeAgentRotation // !!! RUN BEFORE AgentRotators!!!
        InitializeAgentRotation();
        #endregion

        #region AgentRotators
        originalLocalHeadRotation = torso.transform.localRotation; // Save local rotation
        originalLocalHeadRotation = head.transform.localRotation; // Save local rotation
        originalLocalArmAndGunRotation = arms.transform.localRotation; // Store initial local rotation
        #endregion

        #region StayState
        agentStayPosition = transform.position;
        #endregion

        #region AvoidancePriority
        avoidancePriority = Random.value;
        #endregion

        #region NavArea

        #endregion
    }

    private void Start()
    {
        agent.autoBraking = true; // otherwise, the returnposition does not work
        timer = interval;
    }
    
    private void Update()
    {
        CheckEffectiveRange();
        TargetDetectionSight();
        TargetDetectionFelt();
        TargetDetectionHeard();
        DetectionManager();
        StateLogic();
        UpdateState();
        ActiveStateBehaviour();
        Animations();
        Reset();
        DeathCheck();
    }

    #endregion

    #region StateManagement
    private void StateLogic()
    {
        switch ((targetDetectionLevel, agentTask, targetInEffectiveRange)) // Tuple switching
        {
            case (TargetDetectionLevel.None, AgentTask.None, _):
                agentState = AgentState.None;
                break;

            case (TargetDetectionLevel.None, AgentTask.Stay, _):
                agentState = AgentState.Stay;
                break;

            case (TargetDetectionLevel.None, AgentTask.Patrol, _):
                agentState = AgentState.Patrol;
                break;

            case (TargetDetectionLevel.PassiveSuspicion, _, _):
                agentState = AgentState.SuspectPassively;
                break;

            case (TargetDetectionLevel.ActiveSuspicion, _, _):
                agentState = AgentState.SuspectActively;
                break;

            case (TargetDetectionLevel.DetectionMemory, _, _):
                agentState = AgentState.ChaseTarget;
                break;

            case (TargetDetectionLevel.DetectingTarget, _, false):
                agentState = AgentState.ApproachTarget;
                break;

            case (TargetDetectionLevel.DetectingTarget, _, true):
                agentState = AgentState.AttackTarget;
                break;

            default:
                agentState = AgentState.None; // Fallback state
                break;
        }
    }

    private void UpdateState()
    {
        if (agentState != previousAgentState) // Only run when state changes
        {
            OnStateChange(agentState);
            previousAgentState = agentState; // Update previous state tracker
        }
    }

    private void OnStateChange(AgentState newState)
    {
        switch (newState)
        {
            case AgentState.None:
                return;
            case AgentState.Stay:
                ApplyStayValues();
                break;
            case AgentState.Patrol:
                ApplyPatrolValues();
                break;
            case AgentState.SuspectPassively:
                ApplyPassiveSuspicionValues();
                break;
            case AgentState.SuspectActively:
                ApplyActiveSuspicionValues();
                break;
            case AgentState.ChaseTarget:
                ApplyChaseTargetValues();
                break;
            case AgentState.ApproachTarget:
                ApplyApproachTargetValues();
                break;
            case AgentState.AttackTarget:
                ApplyAttackTargetValues();
                break;
        }
    }
    
    private void ActiveStateBehaviour()
    {
        switch (agentState)
        {
            case AgentState.None:
                return;
            case AgentState.Stay:
                StayState();
                break;
            case AgentState.Patrol:
                PatrolState();
                break;
            case AgentState.SuspectPassively:
                PassiveSuspicionState();
                break;
            case AgentState.SuspectActively:
                ActiveSuspicionState();
                break;
            case AgentState.ChaseTarget:
                ChaseTargetState();
                break;
            case AgentState.ApproachTarget:
                ApproachTargetState();
                break;
            case AgentState.AttackTarget:
                AttackTargetState();
                break;
        }

        //Debug.Log("ActiveState: "+ agentState);
    }
    #endregion

    #region StateMethods //...........StateMethods
    private void PatrolState()
    {
        AgentRotatorObjectToMovementDirection();
        AgentRotatorTorsoToMovementDirection();
        AgentRotatorHeadToMovementDirection();
        AgentRotatorArmsToLoweredPosition();
        AgentFollowPatrolPoints();
        ApplyLocalAgentAvoidance();
    }

    private void StayState()
    {
        AgentRotatorObjectToMovementDirection();
        AgentRotatorTorsoToMovementDirection();
        ApplyLocalAgentAvoidance();
    }

    private void PassiveSuspicionState()
    {
        AgentRotatorObjectToMovementDirection(); // not "within movement limits" because movement direction could be a problem at this time
        AgentRotatorTorsoToSuspicionDirection();
        AgentRotatorHeadToSuspicionDirection();
        AgentRotatorArmsToLoweredPosition();
        ApplyLocalAgentAvoidance();
    }


    private Vector3 activeSuspicionStartPosition;    // wo ActiveSuspicion begonnen hat
    private Vector3 frozenSuspicionPosition;         // letzter BestSpot aus Phase A
    private float suspicionWaitTimer;

    // Suspicion / Sweep-Assist
    [SerializeField] private float suspicionSweepWeight = 0.65f; // falls du tweaken willst
    [SerializeField] private float suspicionWaitDuration = 2f;

    private bool activeSuspicionInitialized;
    private bool suspicionMemoryInitialized;

    private enum SuspicionRoutePhase { None, ToSuspicion, Wait, Back }
    private SuspicionRoutePhase suspicionRoutePhase = SuspicionRoutePhase.None;

    private bool hasLastSuspicionBest;
    private Vector3 lastSuspicionBest;


    private void ActiveSuspicionState()
    {
        // NavAreas holen
        NavArea playerArea = null;
        if (target != null)
            NavArea.TryGetAreaAtPosition(target.transform.position, out playerArea);

        bool sameArea = (CurrentNavArea != null && playerArea != null && CurrentNavArea == playerArea);

        // ============= FALL 1: SELBE NAVAREA -> ursprünglicher Pfad =============
        if (sameArea)
        {
            float distanceToSuspicionPosition = Vector3.Distance(transform.position, suspicionPosition);
            float distanceToReturnPosition = Vector3.Distance(transform.position, agentReturnPosition);

            if (!suspicionPositionReached)
            {
                AgentSetDestinationToSuspicionPosition();

                AgentRotatorLegsToMovementDirection();
                AgentRotatorTorsoToSuspicionDirection();
                AgentRotatorHeadToSuspicionDirection();
                AgentRotatorArmsToHalfReadyPosition();

                if (distanceToSuspicionPosition <= satisfactionDistance)
                {
                    suspicionPositionReached = true;
                    AgentSetDestinationToReturnPosition();
                }
            }
            else if (distanceToReturnPosition > returnDistance)
            {
                AgentSetDestinationToReturnPosition();

                AgentRotatorLegsToMovementDirection();
                AgentRotatorTorsoToMovementDirection();
                AgentRotatorHeadSearchContinuousRotation();
                AgentRotatorArmsToLoweredPosition();
            }
            else
            {
                agentNeed = Need.None;
            }

            ApplyLocalAgentAvoidance();
            return;
        }

        // ============= FALL 2: ANDERE NAVAREA =============

        // -------- Phase A: ActiveSuspicion (live Sweeper, letzte valide Position merken) --------
        if (detectionManagerLevel == DetectionManagerLevel.ActiveSuspicion)
        {
            if (!activeSuspicionInitialized)
            {
                activeSuspicionInitialized = true;
                suspicionMemoryInitialized = false;
                suspicionRoutePhase = SuspicionRoutePhase.None;

                activeSuspicionStartPosition = transform.position;
                hasLastSuspicionBest = false; // neu starten
            }

            // Sweeper bestimmt/aktualisiert den BestSpot
            positionCheckerSweepAssistantScript.ExecuteSweepPositionAssistance(
                agent,
                CurrentNavArea,
                targetTransform,
                0.65f);

            // Versuchen, aktuellen BestSpot vom Assistant zu holen
            if (positionCheckerSweepAssistantScript.TryGetCurrentBestSpot(agent, out var bestNow))
            {
                // Immer überschreiben -> wir wollen die LETZTE valide Position konservieren
                lastSuspicionBest = bestNow;
                hasLastSuspicionBest = true;
            }
            else if (!hasLastSuspicionBest)
            {
                // WICHTIG:
                // Wir sind frisch in ActiveSuspicion, haben aber noch KEINEN validen Spot gesehen.
                // Dann macht dieser Modus keinen Sinn -> sauber raus.
                activeSuspicionInitialized = false;
                detectionManagerLevel = DetectionManagerLevel.PassiveSuspicion;
                agentNeed = Need.None;
                return;
            }
            // else:
            // Kein neuer BestSpot, aber wir haben eine alte valide Position.
            // Der NavMeshAgent hat das Ziel bereits vom letzten gültigen BestSpot gesetzt,
            // also lassen wir ihn einfach weiterlaufen. Keine neue Entscheidung nötig.

            // Pose/Rotation während er der (vom Assistant gesetzten) Suspicion-Position folgt
            AgentRotatorLegsToMovementDirection();
            AgentRotatorTorsoToSuspicionDirection();
            AgentRotatorHeadToSuspicionDirection();
            AgentRotatorArmsToHalfReadyPosition();
            ApplyLocalAgentAvoidance();
            return;
        }

        // -------- Phase B: ActiveSuspicionMemory --------
        if (detectionManagerLevel == DetectionManagerLevel.ActiveSuspicionMemory)
        {
            // Nur im ersten Frame dieser Phase initialisieren
            if (!suspicionMemoryInitialized)
            {
                suspicionMemoryInitialized = true;
                activeSuspicionInitialized = false;

                // KEINE neue Position mehr bestimmen.
                // Nur noch die letzte valide Position aus Phase A verwenden.
                if (hasLastSuspicionBest)
                {
                    frozenSuspicionPosition = lastSuspicionBest;

                    suspicionRoutePhase = SuspicionRoutePhase.ToSuspicion;
                    suspicionWaitTimer = suspicionWaitDuration;

                    agent.SetDestination(frozenSuspicionPosition);
                }
                else
                {
                    // Es gab nie einen gültigen BestSpot -> direkt zurück.
                    suspicionRoutePhase = SuspicionRoutePhase.Back;
                    suspicionWaitTimer = 0f;

                    agent.SetDestination(activeSuspicionStartPosition);
                }
            }

            switch (suspicionRoutePhase)
            {
                // 1) Hin zur gefrorenen Suspicion-Position
                case SuspicionRoutePhase.ToSuspicion:
                    {
                        float thresh = Mathf.Max(agent.stoppingDistance, satisfactionDistance);

                        if (!agent.pathPending && agent.remainingDistance <= thresh)
                        {
                            agent.ResetPath();
                            suspicionRoutePhase = SuspicionRoutePhase.Wait;
                            suspicionWaitTimer = suspicionWaitDuration;
                        }

                        AgentRotatorLegsToMovementDirection();
                        AgentRotatorTorsoToSuspicionDirection();
                        AgentRotatorHeadToSuspicionDirection();
                        AgentRotatorArmsToHalfReadyPosition();
                        ApplyLocalAgentAvoidance();
                        return;
                    }

                // 2) Dort warten
                case SuspicionRoutePhase.Wait:
                    {
                        suspicionWaitTimer -= Time.deltaTime;
                        if (suspicionWaitTimer <= 0f)
                        {
                            suspicionRoutePhase = SuspicionRoutePhase.Back;
                            agent.SetDestination(activeSuspicionStartPosition);
                        }

                        // Wachsame/half-ready Pose am Spot
                        AgentRotatorLegsToMovementDirection();
                        AgentRotatorTorsoToSuspicionDirection();
                        AgentRotatorHeadToSuspicionDirection();
                        AgentRotatorArmsToHalfReadyPosition();
                        ApplyLocalAgentAvoidance();
                        return;
                    }

                // 3) Zurück zur Startposition
                case SuspicionRoutePhase.Back:
                    {
                        float thresh = Mathf.Max(agent.stoppingDistance, returnDistance);

                        if (!agent.pathPending && agent.remainingDistance <= thresh)
                        {
                            agent.ResetPath();

                            suspicionRoutePhase = SuspicionRoutePhase.None;
                            suspicionMemoryInitialized = false;
                            activeSuspicionInitialized = false;
                            hasLastSuspicionBest = false;

                            detectionManagerLevel = DetectionManagerLevel.None;
                            agentNeed = Need.None;

                            AgentRotatorLegsToMovementDirection();
                            AgentRotatorTorsoToMovementDirection();
                            AgentRotatorHeadSearchContinuousRotation();
                            AgentRotatorArmsToLoweredPosition();
                            ApplyLocalAgentAvoidance();
                            return;
                        }

                        AgentRotatorLegsToMovementDirection();
                        AgentRotatorTorsoToMovementDirection();
                        AgentRotatorHeadSearchContinuousRotation();
                        AgentRotatorArmsToLoweredPosition();
                        ApplyLocalAgentAvoidance();
                        return;
                    }
            }

            // Fallback falls irgendwas inkonsistent ist
            detectionManagerLevel = DetectionManagerLevel.PassiveSuspicion;
            agentNeed = Need.None;
            suspicionRoutePhase = SuspicionRoutePhase.None;
            suspicionMemoryInitialized = false;
            activeSuspicionInitialized = false;
            hasLastSuspicionBest = false;
            ApplyLocalAgentAvoidance();
            return;
        }

        // Andere Detection-Levels -> Suspicion-Flags zurücksetzen
        activeSuspicionInitialized = false;
        suspicionMemoryInitialized = false;
        suspicionRoutePhase = SuspicionRoutePhase.None;
        hasLastSuspicionBest = false;
    }







    private void ChaseTargetState()
    {
        AgentSetDestinationToTarget();
        AgentRotatorLegsToMovementDirection();
        AgentRotatorTorsoToTargetDirectionNoLimits();
        AgentRotatorHeadToTargetDirection();
        AgentRotatorArmsToHalfReadyPosition();
        ApplyLocalAgentAvoidance();
        ApplyTargetAvoidance();
    }

    private void ApproachTargetState()
    {
        AgentSetDestinationToTarget();
        AgentRotatorLegsToMovementDirection();
        AgentRotatorTorsoToTargetDirectionNoLimits();
        AgentRotatorHeadToTargetDirection();
        AgentRotatorArmsToTargetDirection();
        ApplyLocalAgentAvoidance();
        ApplyTargetAvoidance();
    }

    private void AttackTargetState()
    {
        AgentSetDestinationToTarget();
        AgentRotatorLegsToMovementDirection();
        AgentRotatorTorsoToTargetDirectionNoLimits();
        AgentRotatorHeadToTargetDirection();
        AgentRotatorArmsToTargetDirection();
        ApplyLocalAgentAvoidance();
        ApplyTargetAvoidance();
    }

    #endregion

    #region ApplyValuesMethods
    private void ApplyStayValues()
    {
        AgentSetDestinationToStayPosition();
        agent.stoppingDistance = stoppingDistanceStay;
        agent.speed = 3f;
        //Debug.Log("Agent is now staying.");
        if (!isDead)
        {
        stateAudioScript.PlayExclusiveAudio(-1);
        }
    }

    private void ApplyPatrolValues()
    {
        agent.speed = 2f;
        agent.stoppingDistance = 0.3f;

        //Debug.Log("Agent is now patrolling.");
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(-1);
        }
    }

    private void ApplyPassiveSuspicionValues()
    {
        agent.speed = 2f;
        AgentSetReturnPositionToSelf();
        AgentSetDestinationToSelf();
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(0);
        }
    }

    private void ApplyActiveSuspicionValues()
    {
        suspicionPositionReached = false;
        agent.stoppingDistance = 0.3f;
        agent.speed = 3.5f;
        AgentSetReturnPositionToSelf();
        AgentSetDestinationToSuspicionPosition();
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(1);
        }
    }

    private void ApplyChaseTargetValues()
    {
        agent.stoppingDistance = 0.5f;
        agent.speed = 5f;
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(2);
        }
    }

    private void ApplyApproachTargetValues()
    {
        agent.stoppingDistance = 2f;
        agent.speed = 5f;
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(2);
        }
    }

    private void ApplyAttackTargetValues()
    {
        agent.stoppingDistance = 200f;//effectiveRange * 4f; ?
        agent.speed = 5f;
        if (!isDead)
        {
            stateAudioScript.PlayExclusiveAudio(2);
        }
    }
    #endregion

    #region TargetDetectionAndDetectionManagement

    private void CheckEffectiveRange()
    {
        CalculateDistanceToTarget();
        if (distanceToTarget <= effectiveRange)
        {
            targetInEffectiveRange = true;
        }
        else
        {
            targetInEffectiveRange = false;
        }

        if (distanceToTarget <= shotRange)
        {
            targetInShotRange = true;
        }
        else
        {
            targetInShotRange = false;
        }
    }

    private void DetectionManager()
    {   
        //Debug.Log(agentTargetSightMeter);
        if (sightDetection == SightDetection.DetectingTarget || feltDetection == FeltDetection.DetectingTarget || heardDetection == HeardDetection.DetectingTarget)             // detection
        {
            if (sightDetection == SightDetection.DetectingTarget)
            {
                PeriodicCallout();
            }
            // Detection has the highest priority -> Reset all other states
            detectionManagerLevel = DetectionManagerLevel.DetectingTarget;
            detectionHoldTimer = detectionHoldDuration;
            detectionMemoryTimer = detectionMemoryDuration;
            activeSuspicionMemoryTimer = 0f;
            passiveSuspicionMemoryTimer = 0f;

            agentNeed = Need.AttackTarget;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.DetectingTarget)                                               // detection memory initialisation
        {
            if (sightDetection == SightDetection.DetectingTarget)
            {
                PeriodicCallout();
            }
            // Detection Memory starts when target is lost
            detectionHoldTimer -= Time.deltaTime;
            //Debug.Log("DetectionHoldTimer: " + detectionHoldTimer);
            if (detectionHoldTimer <= 0f)
            {
                detectionMemoryTimer = detectionMemoryDuration;
                detectionManagerLevel = DetectionManagerLevel.DetectionMemory;
                activeSuspicionMemoryTimer = 0f;
                passiveSuspicionMemoryTimer = 0f;
            }
            agentNeed = Need.ReachTarget;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.DetectionMemory)                                            // detection memory
        {
            #region
            // Continue detection memory countdown
            detectionMemoryTimer -= Time.deltaTime;
            //Debug.Log("DetectionMemoryTimer: " + detectionMemoryTimer);
            if (detectionMemoryTimer <= 0f)
            {
                //Debug.Log("FailedTargetDetection");
                detectionManagerLevel = DetectionManagerLevel.ActiveSuspicion; // Memory expired 
            }
            #endregion
            //if (agentNeed == Need.None && previousAgentNeed == Need.ReachTarget)
            //{
            //    detectionManagerLevel = DetectionManagerLevel.None;
            //}
        }
        else if (sightDetection == SightDetection.ActiveSuspicion || feltDetection == FeltDetection.ActiveSuspicion || heardDetection == HeardDetection.ActiveSuspicion)            //active suspicion
        {
            suspicionPosition = target.transform.position;

            // Active Suspicion takes over if detection memory expired
            detectionManagerLevel = DetectionManagerLevel.ActiveSuspicion;
            activeSuspicionMemoryTimer = activeSuspicionMemoryDuration;
            passiveSuspicionMemoryTimer = 0f;

            agentNeed = Need.SatisfyActiveSuspicion;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.ActiveSuspicion)
        {
            suspicionPosition = target.transform.position;

            detectionManagerLevel = DetectionManagerLevel.ActiveSuspicionMemory;
            agentNeed = Need.SatisfyActiveSuspicion;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.ActiveSuspicionMemory)
        {
            if (agentNeed != Need.SatisfyActiveSuspicion && previousAgentNeed == Need.SatisfyActiveSuspicion)
            {
                detectionManagerLevel = DetectionManagerLevel.None;
            }
            #region TimerVariant
            // Active suspicion countdown
            //activeSuspicionMemoryTimer -= Time.deltaTime;
            //if (activeSuspicionMemoryTimer <= 0f)
            //{
            //    //Debug.Log("FailedActiveSuspicion");
            //    detectionManagerLevel = DetectionManagerLevel.None; // Memory expired
            //}
            #endregion

        }
        else if (sightDetection == SightDetection.PassiveSuspicion || feltDetection == FeltDetection.PassiveSuspicion || heardDetection == HeardDetection.PassiveSuspicion)
        {
            // Passive Suspicion takes over if no detection or active suspicion
            detectionManagerLevel = DetectionManagerLevel.PassiveSuspicion;
            passiveSuspicionMemoryTimer = passiveSuspicionMemoryDuration;

            suspicionPosition = target.transform.position;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.PassiveSuspicion)
        {
            // Passive Suspicoon Memory starts when target is lost
            detectionManagerLevel = DetectionManagerLevel.PassiveSuspicionMemory;

            suspicionPosition = target.transform.position;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.PassiveSuspicionMemory)
        {
            if (agentNeed == Need.None && previousAgentNeed == Need.SatisfyPassiveSuspicion)
            {
                detectionManagerLevel = DetectionManagerLevel.None;
            }
            #region TimerVariant
            // Passive suspicion countdown
            passiveSuspicionMemoryTimer -= Time.deltaTime;
            if (passiveSuspicionMemoryTimer <= 0f)
            {
                //Debug.Log("FailedPassiveSuspicion");
                detectionManagerLevel = DetectionManagerLevel.None; // Memory expired
            }
            #endregion
        }
        else
        {
            detectionManagerLevel = DetectionManagerLevel.None; // Default state when all timers expire
        }

        previousAgentNeed = agentNeed; // extremely important to be executed after detection evaluation and before state method application.
                                       // saves what the Det.Manager used to evaluate its suspicionmemory in this frame so it can be compared in the next frame

        #region FinalDecisionDetectionLogic
        // Final Decision about TargetDetectionLevel for state logic
        if (detectionManagerLevel == DetectionManagerLevel.DetectingTarget)
        {
            targetDetectionLevel = TargetDetectionLevel.DetectingTarget;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.DetectionMemory)
        {
            targetDetectionLevel = TargetDetectionLevel.DetectionMemory;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.ActiveSuspicion || detectionManagerLevel == DetectionManagerLevel.ActiveSuspicionMemory)
        {
            targetDetectionLevel = TargetDetectionLevel.ActiveSuspicion;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.PassiveSuspicion || detectionManagerLevel == DetectionManagerLevel.PassiveSuspicionMemory)
        {
            targetDetectionLevel = TargetDetectionLevel.PassiveSuspicion;
        }
        else if (detectionManagerLevel == DetectionManagerLevel.None)
        {
            targetDetectionLevel = TargetDetectionLevel.None;
        }
        #endregion

        //    Debug.Log($"Target Detection Level Updated: {targetDetectionLevel} | " +
        //$"DetectionManagerLevel: {detectionManagerLevel} | " +
        //$"DetectsTarget: {targetDetectionLevel == TargetDetectionLevel.DetectingTarget} | " +
        //$"DetectionMemory: {targetDetectionLevel == TargetDetectionLevel.DetectionMemory} | " +
        //$"ActiveSuspicion: {targetDetectionLevel == TargetDetectionLevel.ActiveSuspicion} | " +
        //$"PassiveSuspicion: {targetDetectionLevel == TargetDetectionLevel.PassiveSuspicion} | " +
        //$"DoesNotDetect: {targetDetectionLevel == TargetDetectionLevel.None}");
    }

    #region SightDetection
    private void TargetDetectionSight()
    {
        if (sightDetectionCloseScript.targetInSightCloseMultiplier > 0 ||
            sightDetectionCenterScript.targetInSightCenterMultiplier > 0 ||
            sightDetectionPeripheralScript.targetInSightPeripheralMultiplier > 0)
        {
            if (distanceToTarget <= maxDetectionSightDistance)
            {
                float distanceMultiplier = Mathf.Clamp(1 - (distanceToTarget / maxDetectionSightDistance), 0f, 1f);
                float sightDetCloRate = sightDetCloseBaseRate * sightDetectionCloseScript.targetInSightCloseMultiplier * distanceMultiplier;
                float sightDetCenRate = sightDetCenterBaseRate * sightDetectionCenterScript.targetInSightCenterMultiplier * distanceMultiplier;
                float sightDetPerRate = sightDetPeriphBaseRate * sightDetectionPeripheralScript.targetInSightPeripheralMultiplier * distanceMultiplier;

                // Raycast from sightSourceTransform to the target
                RaycastHit hit;
                Vector3 directionToTarget = (targetTransform.position - sightSourceTransform.position).normalized;
                float rayDistance = Vector3.Distance(sightSourceTransform.position, targetTransform.position);
                #region RaycastDebugger
                // Debug DrawRay to visualize raycasting in the Scene view
                //Debug.DrawRay(sightSourceTransform.position, directionToTarget * rayDistance, Color.red, 0.1f);
                //
                //if (Physics.Raycast(sightSourceTransform.position, directionToTarget, out hit, rayDistance))
                //{
                //    Debug.Log("Raycast hit: " + hit.collider.gameObject.name);
                //}
                //else
                //{
                //    Debug.Log("Raycast did not hit anything!");
                //}
                #endregion
                if (Physics.Raycast(sightSourceTransform.position, directionToTarget, out hit, rayDistance, layerMask)) // ,obstacleLayer)
                {
                    if (hit.collider.gameObject == targetShell)
                    {
                        //Debug.Log("Raycast hit the target!");
                        agentTargetSightMeter += (sightDetCloRate + sightDetCenRate + sightDetPerRate) * Time.deltaTime;

                        if(agentNeed == Need.ReachTarget || agentNeed == Need.AttackTarget) // makes the agent detect the player instantly when seeing him in a chase
                        {
                            agentTargetSightMeter = 1f;
                        }
                    }
                    else
                    {
                        agentTargetSightMeter = Mathf.Max(0, agentTargetSightMeter - Time.deltaTime * agentTargetMaxSightMeter * sightRayDetectionDecayRate); // decay rates should be the same to be sensible, but can be changed for balancing later
                        //Debug.Log("Raycast blocked by: " + hit.transform.name);
                        //Debug.Log("Raycast failed");
                        targetInEffectiveRange = false; 
                    }
                }
            }
            else
            {
                //Debug.Log("DistanceFailed");
                agentTargetSightMeter = Mathf.Max(0, agentTargetSightMeter - Time.deltaTime * agentTargetMaxSightMeter * sightDistanceDetectionDecayRate); // decay rates should be the same to be sensible, but can be changed for balancing later
                targetInEffectiveRange = false; 
            }
        }
        else
        {
            //Debug.Log("CollidersFailed");
            agentTargetSightMeter = Mathf.Max(0, agentTargetSightMeter - Time.deltaTime * agentTargetMaxSightMeter * sightColliderDetectionDecayRate); // decay rates should be the same to be sensible, but can be changed for balancing later
            targetInEffectiveRange = false; 
        }

        // Ensure it stays within bounds (0 to maxDetectionMeter)
        agentTargetSightMeter = Mathf.Clamp(agentTargetSightMeter, 0, agentTargetMaxSightMeter); // clamp of the sightDetectionmeter

        if (agentTargetSightMeter >= agentTargetMaxSightMeter)
        {
            sightDetection = SightDetection.DetectingTarget;
        }
        else if (agentTargetSightMeter >= agentTargetSightActiveSuspicionTreshhold)
        {
            sightDetection = SightDetection.ActiveSuspicion;
        }
        else if (agentTargetSightMeter >= agentTargetSightPassiveSuspicionTreshhold)
        {
            sightDetection = SightDetection.PassiveSuspicion;
        }
        else if (agentTargetSightMeter < agentTargetSightPassiveSuspicionTreshhold)
        {
            sightDetection = SightDetection.None;
        }
        //Debug.Log("agentTargetSightMeter =" + agentTargetSightMeter + "| agentTargetInSight =" + agentTargetInSight + "| passive =" + agentTargetInSightPassiveSuspicion + "| active =" + agentTargetInSightActiveSuspicion);
    }
    #endregion

    #region FeltDetection
    private void TargetDetectionFelt()
    {
        if (feltDetectionCloseScript.targetInFeltCloseMultiplier > 0)
        {
            float distanceMultiplier = Mathf.Clamp(1 - (distanceToTarget / maxDetectionFeltDistance), 0f, 1f);
            float feltDetCloRate = feltDetCloseBaseRate * feltDetectionCloseScript.targetInFeltCloseMultiplier * distanceMultiplier;
            agentTargetFeltMeter += feltDetCloRate * Time.deltaTime;
            // Ensure it stays within bounds (0 to maxDetectionMeter)
            agentTargetFeltMeter = Mathf.Clamp(agentTargetFeltMeter, 0, agentTargetMaxFeltMeter);
        }
        else
        {
            agentTargetFeltMeter = Mathf.Max(0, agentTargetFeltMeter - Time.deltaTime * agentTargetMaxFeltMeter * feltColliderDetectionDecayRate);
        }

        if (agentTargetFeltMeter >= agentTargetMaxFeltMeter)
        {
            feltDetection = FeltDetection.DetectingTarget;
        }
        else if (agentTargetFeltMeter >= agentTargetFeltActiveSuspicionTreshhold)
        {
            feltDetection = FeltDetection.ActiveSuspicion;
        }
        else if (agentTargetFeltMeter >= agentTargetFeltPassiveSuspicionTreshhold)
        {
            feltDetection = FeltDetection.PassiveSuspicion;
        }
        else if (agentTargetFeltMeter < agentTargetFeltPassiveSuspicionTreshhold)
        {
            feltDetection = FeltDetection.None;
        }
        //Debug.Log("agentTargetFeltMeter =" + agentTargetFeltMeter + "| agentTargetInFelt =" + agentTargetInFelt + "|");
    }
    #endregion

    #region DetectionHeard
    private void TargetDetectionHeard()
    {
            float heardDetRate = heardDetBaseRate * heardSoundintensity;
            agentTargetHeardMeter += heardDetRate * Time.deltaTime;
            // Ensure it stays within bounds (0 to maxDetectionMeter)
            agentTargetHeardMeter = Mathf.Clamp(agentTargetHeardMeter, 0, agentTargetMaxHeardMeter);
        
        if (agentTargetHeardMeter > 0)
        {
            agentTargetHeardMeter = Mathf.Max(0, agentTargetHeardMeter - Time.deltaTime * agentTargetMaxHeardMeter * heardDetectionDecayRate);
        }

        //if (agentTargetHeardMeter >= 0.75f) // not suitable for current design
        //{
        //    heardDetection = HeardDetection.DetectingTarget;
        //}
        //else
        if (agentTargetHeardMeter >= 0.5f)
        {
            heardDetection = HeardDetection.ActiveSuspicion;
        }
        else if (agentTargetHeardMeter >= 0.25f)
        {
            heardDetection = HeardDetection.PassiveSuspicion;
        }
        else if (agentTargetHeardMeter < 0.25f)
        {
            heardDetection = HeardDetection.None;
        }
        //Debug.Log("agentTargetHeardMeter =" + agentTargetHeardMeter + "| heardetectionLevel =" + heardDetection + "|");
    }

    private void OnEnable()
    {
        //Debug.Log(" Agent enabled! Subscribing to sound event.");
        //SoundManager.OnHighSoundPlayed += DetectHighSound; // Start listening
        SoundManager.OnSoundPlayed += DetectSound; // Start listening
        //SoundManager.OnLowSoundPlayed += DetectLowSound; // Start listening

    }

    private void OnDisable()
    {
        SoundManager.OnSoundPlayed -= DetectSound; // Start listening
    }

    private void DetectSound(Vector3 soundPosition, float maxRadius, float baseIntensity)
    {
        float distance = Vector3.Distance(transform.position, soundPosition);

        if (distance > maxRadius)
            return; // Ignore sounds outside range

        //  Calculate intensity based on distance (linear falloff)
        heardSoundintensity = baseIntensity * (1 - (distance / maxRadius));

        //Debug.Log($"NPC heard mid sound! Intensity: {heardSoundintensity:F2} at distance: {distance:F2}");

        //  React based on intensity (e.g., different behavior for loud vs quiet sounds)
        //if (heardSoundintensity > 0.75f)
        //{
        //    Debug.Log("NPC strongly reacts to the sound!");
        //
        //}
        //else if (heardSoundintensity > 0.5f)
        //{
        //    Debug.Log("NPC notices the sound but isn’t alarmed.");
        //
        //}
        //else if(heardSoundintensity > 0.25f)
        //{
        //    Debug.Log("NPC barely hears it.");
        //
        //}
        //else
        //{
        //    Debug.Log("NPC doesnt near it.");
        //}
    }
    #endregion

    #endregion

    #region TestMethods

    #endregion

    #region UniversalBehaviourAndActionMethods

    private void CalculateDistanceToTarget()
    {
        distanceToTarget = Vector3.Distance(transform.position, targetTransform.position);
    }

    public void ReceivePistolDamage()
    {
        //Debug.Log($" Agent: Damage received! Agent: Current health: {agentHealth}"); // Debugging
        agentHealth -= damagePerParticle;
        agentHealth = Mathf.Clamp(agentHealth, 0, agentMaxHealth);

        agentHitSound.Play();

        //Debug.Log($"Agent: New health: {agentHealth}"); // Debugging
        if(detectionManagerLevel != DetectionManagerLevel.DetectingTarget) //hit by the player gun causes the enemy  to detect the player
        {
            agentTargetSightMeter = 1f; 
            detectionManagerLevel = DetectionManagerLevel.DetectingTarget;
        }
    }

    public void ReceivePistolCritDamage()
    {
        //Debug.Log($" Agent: Damage received! Agent: Current health: {agentHealth}"); // Debugging
        agentHealth -= 25f;
        agentHealth = Mathf.Clamp(agentHealth, 0, agentMaxHealth);
        //Debug.Log($"Agent: New health: {agentHealth}"); // Debugging
        if (detectionManagerLevel != DetectionManagerLevel.DetectingTarget) //hit by the player gun causes the enemy  to detect the player
        {
            agentTargetSightMeter = 1f;
            detectionManagerLevel = DetectionManagerLevel.DetectingTarget;
        }
    }

    public void ReceiveOneShotDamage()
    {
        //Debug.Log($" Agent: Damage received! Agent: Current health: {agentHealth}"); // Debugging
        agentHealth -= agentMaxHealth;
        agentHealth = Mathf.Clamp(agentHealth, 0, agentMaxHealth);
        //Debug.Log($"Agent: New health: {agentHealth}"); // Debugging
    }
    void CheckAgentPath() // not necessary at the moment
    {
        if (agent.pathPending)
        {
            //Debug.Log("Agent is still calculating the path...");
            return;
        }

        if (!agent.hasPath)
        {
            //Debug.Log("Agent has no path.");
            return;
        }

        switch (agent.path.status)
        {
            case NavMeshPathStatus.PathComplete:
                //Debug.Log("Agent's path is valid and complete.");
                break;

            case NavMeshPathStatus.PathPartial:
                //Debug.Log("Agent's path is **partially** valid. It cannot reach the final destination.");
                break;

            case NavMeshPathStatus.PathInvalid:
                //Debug.Log("Agent's path is **invalid**. No way to reach the target.");
                break;
        }
    }

    private IEnumerator AgentSetDestinationToSuspicion(Vector3 targetPos)
    {
        isCalculatingPath = true; // Mark as running

        NavMeshPath path = new NavMeshPath();
        agent.CalculatePath(targetPos, path);

        while (agent.pathPending)
        {
            yield return null; // Wait until the path is calculated
        }

        Vector3 lastValidPoint = GetLastValidPathPoint(path);

        if (path.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetDestination(targetPos);
            lastDestination = targetPos;
        }
        else if (path.status == NavMeshPathStatus.PathPartial)
        {
            //Debug.Log($" Path is partial. Moving to last valid point: {lastValidPoint}");
            agent.SetDestination(lastValidPoint);
            lastDestination = lastValidPoint;
        }
        else
        {
            //Debug.LogWarning(" No valid path to suspicion position. Staying in place.");
            agent.SetDestination(transform.position);
            lastDestination = transform.position;
        }

        isCalculatingPath = false; // Reset flag when done
    }
    private Vector3 GetNearestNavMeshPoint(Vector3 position, float searchRadius)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, searchRadius, NavMesh.AllAreas))
        {
            return hit.position; // Return closest valid point
        }
        Debug.LogWarning(" No valid NavMesh point found near target suspicion position.");
        return transform.position; // Default to agent's position if no valid point is found
    }
    private Vector3 GetLastValidPathPoint(NavMeshPath path)
    {
        if (path.status == NavMeshPathStatus.PathInvalid || path.corners.Length < 2)
        {
            Debug.LogWarning(" No valid path. Keeping position.");
            return agent.transform.position;
        }
        return path.corners[path.corners.Length - 2]; // Second to last corner
    }

    private void AgentSetDestinationToTarget()
    {
        if (agent == null || target == null)
            return;

        // Direkter, simpler Vergleich der beiden Felder:
        var agentArea = this.CurrentNavArea;
        var playerArea = playerControllerScript.CurrentNavArea;

        bool sameArea = (agentArea != null && playerArea != null && ReferenceEquals(agentArea, playerArea));

        if (sameArea)
        {
            // deine bestehende Logik
            Vector3 targetPos = targetTransform.position;
            if (!isCalculatingPath && (lastDestination - targetPos).sqrMagnitude > 4f)
            {
                agent.SetDestination(targetPos);
                lastDestination = targetPos;
            }
        }
        else
        {
            // anderer oder kein NavArea -> Assist nutzen
            if (positionCheckerSweepAssistantScript != null && agentArea != null)
            {
                positionCheckerSweepAssistantScript.ExecuteSweepPositionAssistance(
                    agent,
                    agentArea,
                    targetTransform,
                    0.65f
                );
            }
            // wenn agentArea null oder kein Assistant: nichts tun (stehenbleiben / altes Ziel behalten)
        }
    }

    private void AgentSetDestinationToSuspicionPosition()
    {
        if (Vector3.Distance(lastDestination, suspicionPosition) > 3f)
        {
            StartCoroutine(AgentSetDestinationToSuspicion(GetNearestNavMeshPoint(suspicionPosition, 10f)));
        }
    }

    private void AgentSetDestinationToStayPosition()
    {
        agent.SetDestination(agentStayPosition);
    }

    private void AgentSetDestinationToSelf()
    {
        agent.SetDestination(transform.position);
    }

    private void AgentSetDestinationToSelfRepeatedly()
    {
        Vector3 selfPos = transform.position;
        if (Vector3.Distance(lastDestination, selfPos) > 2f) // Prevents redundant calls
        {
            agent.SetDestination(selfPos);
            lastDestination = selfPos;
        }
    }

    private void AgentSetDestinationToReturnPosition()
    {
        agent.SetDestination(agentReturnPosition);
    }

    private void AgentSetReturnPositionToSelf()
    {
        agentReturnPosition = transform.position;
    }


    private void AgentRotatorArmsToLoweredPosition()
    {
        // Define the target downward rotation (45-degree downward tilt)
        Quaternion targetLocalRotation = Quaternion.Euler(33f, 0f, 0f) * originalLocalArmAndGunRotation;

        // Apply rotation smoothly in local space
        arms.transform.localRotation = Quaternion.Slerp(
            arms.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorArmsToReadyPosition()
    {
        // Define the wished rotation (0(standard position))
        Quaternion targetLocalRotation = originalLocalArmAndGunRotation;

        // Apply rotation smoothly in local space
        arms.transform.localRotation = Quaternion.Slerp(
            arms.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorArmsToHalfReadyPosition()
    {
        // Define the wished rotation (0(standard position))
        Quaternion targetLocalRotation = Quaternion.Euler(16.5f, 0f, 0f) * originalLocalArmAndGunRotation;

        // Apply rotation smoothly in local space
        arms.transform.localRotation = Quaternion.Slerp(
            arms.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorArmsToSuspicionDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToTarget = suspicionPosition - arms.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = arms.transform.parent.InverseTransformDirection(directionToTarget);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);

        // Limit rotation to maxHeadRotationAngle from original local rotation
        float angle = Quaternion.Angle(originalLocalArmAndGunRotation, targetLocalRotation);
        if (angle > maxarmsRotationAngle)
        {
            targetLocalRotation = Quaternion.Slerp(originalLocalArmAndGunRotation, targetLocalRotation, maxarmsRotationAngle / angle);
        }

        // Apply rotation smoothly in local space
        arms.transform.localRotation = Quaternion.Slerp(
            arms.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorArmsToTargetDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToTarget = target.transform.position - arms.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = arms.transform.parent.InverseTransformDirection(directionToTarget);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);
        Vector3 targetEulerAngles = targetLocalRotation.eulerAngles;

        // Ensure Y rotation does not exceed ±10° from the original rotation
        float yAngleDifference = Mathf.DeltaAngle(originalLocalArmAndGunRotation.eulerAngles.y, targetEulerAngles.y);
        float clampedYRotation = originalLocalArmAndGunRotation.eulerAngles.y + Mathf.Clamp(yAngleDifference, -10f, 10f);

        // Create a final target rotation with only Y-axis changes
        Quaternion finalTargetRotation = Quaternion.Euler(
            targetEulerAngles.x,  // Keep X-axis unchanged
            clampedYRotation,     // Apply clamped Y-axis
            targetEulerAngles.z   // Keep Z-axis unchanged
        );

        // Apply rotation smoothly in local space
        arms.transform.localRotation = Quaternion.Slerp(
            arms.transform.localRotation,
            finalTargetRotation,
            Time.deltaTime * 5f
        );
    }


    #region HeadRotators
    private void AgentRotatorHeadSearchContinuousRotation()
    {
        if (head == null) return;

        // Yaw-Winkel kontinuierlich erhöhen
        currentSearchHeadYawAngle += headSearchYawSpeedDegPerSec * Time.deltaTime;

        // Winkel im Bereich [0, 360) halten, um Überlauf zu vermeiden
        currentSearchHeadYawAngle = Mathf.Repeat(currentSearchHeadYawAngle, 360f);

        // Rotationen im lokalen Raum aufbauen
        Quaternion yaw = Quaternion.AngleAxis(currentSearchHeadYawAngle, Vector3.up);    // links/rechts
        Quaternion pitch = Quaternion.AngleAxis(headSearchPitchAngle, Vector3.right); // hoch/runter (fester Wert)

        // Basisrotation des Kopfes + Yaw + Pitch
        head.transform.localRotation = originalLocalHeadRotation * yaw * pitch;
    }

    private void AgentRotatorHeadToTargetDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToTarget = target.transform.position - head.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = head.transform.parent.InverseTransformDirection(directionToTarget);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);

        // Limit rotation to maxHeadRotationAngle from original local rotation
        float angle = Quaternion.Angle(originalLocalHeadRotation, targetLocalRotation);
        if (angle > maxHeadRotationAngle)
        {
            targetLocalRotation = Quaternion.Slerp(originalLocalHeadRotation, targetLocalRotation, maxHeadRotationAngle / angle);
        }

        // Apply rotation smoothly in local space
        head.transform.localRotation = Quaternion.Slerp(
            head.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorHeadToSuspicionDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToSuspicion = suspicionPosition - head.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = head.transform.parent.InverseTransformDirection(directionToSuspicion);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion suspicionLocalRotation = Quaternion.LookRotation(localDirection);

        // Limit rotation to maxHeadRotationAngle from original local rotation
        float angle = Quaternion.Angle(originalLocalHeadRotation, suspicionLocalRotation);
        if (angle > maxHeadRotationAngle)
        {
            suspicionLocalRotation = Quaternion.Slerp(originalLocalHeadRotation, suspicionLocalRotation, maxHeadRotationAngle / angle);
        }

        // Apply rotation smoothly in local space
        head.transform.localRotation = Quaternion.Slerp(
            head.transform.localRotation,
            suspicionLocalRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorHeadToMovementDirection()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Convert world direction to local space
        Vector3 localDirection = head.transform.parent.InverseTransformDirection(lastAgentDirectionVector);

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);

        // Limit rotation to maxHeadRotationAngle from the original local rotation
        float angle = Quaternion.Angle(originalLocalHeadRotation, targetLocalRotation);
        if (angle > maxHeadRotationAngle)
        {
            targetLocalRotation = Quaternion.Slerp(originalLocalHeadRotation, targetLocalRotation, maxHeadRotationAngle / angle);
        }

        // Check how much the new rotation differs from the last stable rotation
        float angleDifference = Quaternion.Angle(lastStableLocalRotation, targetLocalRotation);

        float dynamicAngleThreshold = Mathf.Lerp(5f, 0.5f, Mathf.Clamp01(agent.velocity.magnitude / 2f)); // if erratic, use "if (angleDifference > 5f) // Old fixed threshold"

        if (angleDifference > dynamicAngleThreshold) // if erratic, use "if (angleDifference > 5f) // Old fixed threshold" instead of those 2 lines
        {
            // If this is the first time detecting this new angle, start the timer
            if (angleCheckStartTime == 0f)
            {
                angleCheckStartTime = Time.time;
            }

            // If the angle has remained stable for the required time, update the stable rotation
            if (Time.time - angleCheckStartTime >= requiredStableTime)
            {
                lastStableLocalRotation = targetLocalRotation; // Commit to the new stable rotation
                angleCheckStartTime = 0f; // Reset timer
            }
        }
        else
        {
            // If angle change is too small, reset the timer
            angleCheckStartTime = 0f;
        }

        // Apply rotation smoothly in local space
        head.transform.localRotation = Quaternion.Slerp(
            head.transform.localRotation,
            lastStableLocalRotation,
            Time.deltaTime * 5f
        );
    }
    #endregion

    #region ObjectRotators

    private void AgentRotatorObjectToTargetDirectionWhenStanding()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            AgentRotatorObjectToTargetDirectionWithinMovementLimits();
        }
        else
        {
            AgentRotatorObjectToTargetDirection();
        }
    }

    private void AgentRotatorObjectToTargetDirectionWithinMovementLimits()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            // Store movement direction
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Calculate direction to target
        Vector3 directionToTarget = (target.transform.position - agent.transform.position).normalized;
        directionToTarget.y = 0f; // Lock Y rotation to prevent tilting

        // Calculate the angle between movement direction and target direction
        float angleToTarget = Vector3.Angle(lastAgentDirectionVector, directionToTarget);

        // Limit rotation to 30 degrees in the direction of movement
        Vector3 limitedDirection = directionToTarget;
        if (angleToTarget > 30f)
        {
            limitedDirection = Vector3.Slerp(lastAgentDirectionVector, directionToTarget, 30f / angleToTarget);
        }

        // Apply rotation smoothly while keeping Z rotation fixed
        Quaternion targetRotation = Quaternion.LookRotation(limitedDirection);
        targetRotation.x = 0f; // Lock X-axis
        targetRotation.z = 0f; // Lock Z-axis

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
    }

    private void AgentRotatorObjectToSuspicionDirectionWithinMovementLimits()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            // Store movement direction
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Calculate direction to suspicion position
        Vector3 directionToSuspicion = (suspicionPosition - agent.transform.position).normalized;
        directionToSuspicion.y = 0f; // Lock Y rotation to prevent tilting

        // Calculate the angle between movement direction and target direction
        float angleToTarget = Vector3.Angle(lastAgentDirectionVector, directionToSuspicion);

        // Limit rotation to 30 degrees in the direction of movement
        Vector3 limitedDirection = directionToSuspicion;
        if (angleToTarget > 30f)
        {
            limitedDirection = Vector3.Slerp(lastAgentDirectionVector, directionToSuspicion, 30f / angleToTarget);
        }

        // Apply rotation smoothly while keeping Z rotation fixed
        Quaternion targetRotation = Quaternion.LookRotation(limitedDirection);
        targetRotation.x = 0f; // Lock X-axis
        targetRotation.z = 0f; // Lock Z-axis

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
    }

    private void AgentRotatorObjectToTargetDirection()
    {
        Vector3 directionToTarget = (target.transform.position - agent.transform.position).normalized;
        directionToTarget.y = 0f; // Lock Y rotation to prevent tilting

        // Apply rotation without affecting Z
        Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
        targetRotation.x = 0f; // Lock X-axis
        targetRotation.z = 0f; // Lock Z-axis

        targetRotation = Quaternion.LookRotation(directionToTarget);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
    }

    private void AgentRotatorObjectToSuspicionDirection()
    {
        Vector3 directionToTarget = (suspicionPosition - agent.transform.position).normalized;

        // Remove X and Z components to keep rotation strictly on the Y-axis
        directionToTarget.y = 0f;

        // Prevent zero vector issues
        if (directionToTarget != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }
    }

    private void AgentRotatorObjectToMovementDirection()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Ensure rotation follows movement but locks Z-axis
        Quaternion targetRotation = Quaternion.LookRotation(lastAgentDirectionVector);
        Quaternion fixedRotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f); // Lock X and Z rotation

        transform.rotation = Quaternion.Slerp(transform.rotation, fixedRotation, Time.deltaTime * 5f);
    }
    private void AgentRotatorLegsToMovementDirection()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Ensure rotation follows movement but locks Z-axis
        Quaternion targetRotation = Quaternion.LookRotation(lastAgentDirectionVector);
        Quaternion fixedRotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f); // Lock X and Z rotation

        legs.transform.rotation = Quaternion.Slerp(transform.rotation, fixedRotation, Time.deltaTime * 60f);
    }
    #endregion

    #region TorsoRotators
    private void AgentRotatorTorsoToTargetDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToTarget = target.transform.position - torso.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = torso.transform.parent.InverseTransformDirection(directionToTarget);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);
        Vector3 targetEulerAngles = targetLocalRotation.eulerAngles;

        // Ensure Y rotation does not exceed ±60° from the original rotation
        float yAngleDifference = Mathf.DeltaAngle(originalLocalTorsoRotation.eulerAngles.y, targetEulerAngles.y);
        float clampedYRotation = originalLocalTorsoRotation.eulerAngles.y + Mathf.Clamp(yAngleDifference, -60f, 60f);

        // Create a final target rotation with only Y-axis changes
        Quaternion finalTargetRotation = Quaternion.Euler(
            originalLocalTorsoRotation.eulerAngles.x,  // Keep X-axis unchanged
            clampedYRotation,                          // Apply clamped Y-axis
            originalLocalTorsoRotation.eulerAngles.z   // Keep Z-axis unchanged
        );

        // Apply only Y-axis rotation using Slerp
        torso.transform.localRotation = Quaternion.Slerp(
            torso.transform.localRotation,
            finalTargetRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorTorsoToTargetDirectionNoLimits()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToTarget = target.transform.position - torso.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = torso.transform.parent.InverseTransformDirection(directionToTarget);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);
        Vector3 targetEulerAngles = targetLocalRotation.eulerAngles;

        // Ensure Y rotation does not exceed ±60° from the original rotation
        float yAngleDifference = Mathf.DeltaAngle(originalLocalTorsoRotation.eulerAngles.y, targetEulerAngles.y);
        float freeYRotation = originalLocalTorsoRotation.eulerAngles.y + yAngleDifference;

        // Create a final target rotation with only Y-axis changes
        Quaternion finalTargetRotation = Quaternion.Euler(
            originalLocalTorsoRotation.eulerAngles.x,  // Keep X-axis unchanged
            freeYRotation,                          // Apply clamped Y-axis
            originalLocalTorsoRotation.eulerAngles.z   // Keep Z-axis unchanged
        );

        // Apply only Y-axis rotation using Slerp
        torso.transform.localRotation = Quaternion.Slerp(
            torso.transform.localRotation,
            finalTargetRotation,
            Time.deltaTime * 5f
        );
    }

    private void AgentRotatorTorsoToSuspicionDirection()
    {
        if (target == null) return; // Ensure target exists

        // Get direction from agent to target
        Vector3 directionToSuspicion = suspicionPosition - torso.transform.position;

        // Convert world direction to local space
        Vector3 localDirection = torso.transform.parent.InverseTransformDirection(directionToSuspicion);
        localDirection.Normalize();

        // Calculate the target local rotation
        Quaternion suspicionLocalRotation = Quaternion.LookRotation(localDirection);
        Vector3 targetEulerAngles = suspicionLocalRotation.eulerAngles;

        // Ensure Y rotation does not exceed ±60° from the original rotation
        float yAngleDifference = Mathf.DeltaAngle(originalLocalTorsoRotation.eulerAngles.y, targetEulerAngles.y);
        float clampedYRotation = originalLocalTorsoRotation.eulerAngles.y + yAngleDifference;//Mathf.Clamp(yAngleDifference, -60f, 60f);

        // Create a final target rotation with only Y-axis changes
        Quaternion finalTargetRotation = Quaternion.Euler(
            originalLocalTorsoRotation.eulerAngles.x,  // Keep X-axis unchanged
            clampedYRotation,                          // Apply clamped Y-axis
            originalLocalTorsoRotation.eulerAngles.z   // Keep Z-axis unchanged
        );

        // Apply only Y-axis rotation using Slerp
        torso.transform.localRotation = Quaternion.Slerp(
            torso.transform.localRotation,
            finalTargetRotation,
            Time.deltaTime * 5f
        );
    }


    private void AgentRotatorTorsoToMovementDirection()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            agentDirectionVector = agent.velocity.normalized;
            lastAgentDirectionVector = agentDirectionVector;
        }

        // Convert world direction to local space
        Vector3 localDirection = torso.transform.parent.InverseTransformDirection(lastAgentDirectionVector);

        // Calculate the target local rotation (only considering Y-axis)
        Quaternion targetLocalRotation = Quaternion.LookRotation(localDirection);
        Vector3 targetEulerAngles = targetLocalRotation.eulerAngles;

        // Ensure Y rotation does not exceed ±60° from the original rotation
        float yAngleDifference = Mathf.DeltaAngle(originalLocalTorsoRotation.eulerAngles.y, targetEulerAngles.y);
        float clampedYRotation = originalLocalTorsoRotation.eulerAngles.y + Mathf.Clamp(yAngleDifference, -60f, 60f);

        // Create a final target rotation with only Y-axis changes
        Quaternion finalTargetRotation = Quaternion.Euler(
            originalLocalTorsoRotation.eulerAngles.x,  // Keep X-axis unchanged
            clampedYRotation,                          // Apply clamped Y-axis
            originalLocalTorsoRotation.eulerAngles.z   // Keep Z-axis unchanged
        );

        // Apply only Y-axis rotation using Slerp
        torso.transform.localRotation = Quaternion.Slerp(
            torso.transform.localRotation,
            finalTargetRotation,
            Time.deltaTime * 5f
        );
    }



    #endregion
    #endregion

    #region AttackMethods
    //private void AgentShootTarget() //xcv
    //{
    //    if (targetInEffectiveRange)
    //    {
    //        AgentFireGun();
    //    }
    //}
    //
    //private void AgentFireGun() //xcv
    //{
    //    gunScript.FireShot_Agent();
    //}

    public void ApplyRecoil()
    {
        //if (gunScript != null)
        //{
        //    // Increase the pitch (upward tilt) by 1 degree per shot and Clamp it within the allowed range
        //    yRotation -= gunScript.verticalRecoil;
        //    yRotation = Mathf.Clamp(yRotation, -maxLookAngle, maxLookAngle);
        //
        //    // Apply horizontal recoil (yaw) with slight randomness
        //    float horizontalRecoil = Random.Range(-gunScript.horizontalRecoil, gunScript.horizontalRecoil);
        //
        //    // Apply the rotation to the camera carrier object
        //    cameraTransform.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
        //    playerRigidbody.MoveRotation(playerRigidbody.rotation * Quaternion.Euler(0f, horizontalRecoil, 0f));
        //
        //}
    }

    #endregion

    #region PatrolMethods
    private void AgentFollowPatrolPoints()
    {
        if (patrolPoints.Count == 0) return;

        agent.SetDestination(patrolPoints[currentPatrolIndex].position);

        if (!agent.pathPending && agent.remainingDistance <= 3f)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Count;
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }
    #endregion

    #region SetUpMethods
    private void InitializeAgentRotation() // necessary for navmeshagent and object-rotation to work together
    {
        lastAgentDirectionVector = transform.forward.normalized;
    }
    #endregion

    #region AnimationMethods

    private void Animations()
    {
        TrackSpeedForAnim();
    }

    private void TrackSpeedForAnim()
    {
        if (agent.velocity.magnitude > 0.01f)
        {
            AnimateLegsWalk();
        }
        else
        {
            AnimateLegsIdle();
        }
    }

    private void AnimateLegsWalk()
    {
        legsAnimator.SetBool("bot_isWalking",true);
        legsAnimator.SetFloat("bot_walkSpeed", speedReaderScript.speed * agentAnimWalkSpeedFactor);
    }

    private void AnimateLegsIdle()
    {
        legsAnimator.SetBool("bot_isWalking", false);
    }
    #endregion

    #region DeathMethods

    public void DeathCheck()
    {
        if (agentHealth <= 0 && !isDead)
        {
            isDead = true;
            
            StartCoroutine(HandleDeath());
        }
    }

    private IEnumerator HandleDeath()
    {
        // Disable visuals and colliders
        abstractColliders.SetActive(false);
        physicsColliders.SetActive(false);
        partsAndColliders.SetActive(false);
        stateAudioScript.StopAllAudio();
        randomSoundsScript.StopAllAudio();

        deathSound.Play();

        // Play death VFX
        if (DeathParticles != null)
        {
            DeathParticles.Play();
        }

        // Wait for death cleanup time
        yield return new WaitForSeconds(deathCleanupDelay);

        // Deactivate the entire GameObject
        SoundManager.PlaySound(transform.position,20f, 20f);
        gameObject.SetActive(false);
    }
    #endregion

    #region CalloutMethod
    private void PeriodicCallout()
    {
        Vector3 targetPos = target.transform.position;

        // Run this check every frame, but only act if timer has elapsed
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            //Debug.Log("X");
            SoundManager.PlaySound(targetPos, 100f, 100f);
            timer = interval; // reset timer
        }
    }
    #endregion

    #region NavigationManipulation
    #region AvoidanceMethod
    private void ApplyLocalAgentAvoidance()
    {
        // Nur wenn der Agent wirklich unterwegs ist
        //if (!agent.hasPath) // xcv funktioneirt nicht, wenn agents, die stationär sind nicht beim avoidanceverhalten ignoriert werden
        //    return;

        Vector3 origin = transform.position;

        // Alle potentiellen Nachbarn im Avoidance-Radius erfassen
        Collider[] hits = Physics.OverlapSphere(origin, avoidanceOuterRadius, agentAvoidanceMask);

        Transform closest = null;
        AgentController closestAgent = null;
        float closestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            AgentController otherAgent = hits[i].GetComponentInParent<AgentController>();
            if (otherAgent == null || otherAgent == this)
                continue;

            // Einseitig: nur ausweichen, wenn WIR die niedrigere Priority haben.
            // Der mit höherer Priority läuft stumpf weiter.
            if (this.AvoidancePriority >= otherAgent.AvoidancePriority)
                continue;

            Transform other = otherAgent.transform;
            float dist = Vector3.Distance(origin, other.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = other;
                closestAgent = otherAgent;
            }
        }

        // Kein relevanter Agent in Reichweite
        if (closest == null || closestAgent == null)
            return;

        // Richtung zum nächsten Agenten
        Vector3 toOther = closest.position - origin;
        float distance = toOther.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector3 toOtherDir = toOther / distance;

        // Pro Begegnung eine stabile Rechts/Links-Entscheidung:
        // Nutzt beide InstanceIDs -> für dieses Paar konstant, nicht frameweise random.
        int pairHash = this.GetInstanceID() ^ closestAgent.GetInstanceID();
        float sideSign = ((pairHash & 1) == 0) ? 1f : -1f; // +1 oder -1

        // Seitliche Richtung (rechts/links relativ zur Verbindungslinie)
        Vector3 sideDir = Vector3.Cross(Vector3.up, toOtherDir) * sideSign;

        // Basisrichtung: weg vom anderen + etwas seitlich
        Vector3 awayDir = -toOtherDir;

        if (sideStepStrength > 0f && sideDir.sqrMagnitude > 0.0001f)
        {
            sideDir.Normalize();
            awayDir = (awayDir + sideDir * sideStepStrength).normalized;
        }

        // Stärke der Avoidance linear zwischen outer und inner Radius:
        // - außerhalb outerRadius: 0
        // - bei innerRadius oder näher: 1
        float tBase = Mathf.Clamp01(
            (avoidanceOuterRadius - distance) / (avoidanceOuterRadius - avoidanceInnerRadius)
        );

        // Zielgeschwindigkeit fürs Ausweichen
        Vector3 runAwayVelocity = awayDir * agent.speed * avoidanceSpeedMultiplier * avoidanceStrength;

        // Blend-Faktor
        float t = Mathf.Clamp01(tBase * avoidanceStrength);

        agent.velocity = Vector3.Lerp(
            agent.desiredVelocity,
            runAwayVelocity,
            t
        );
    }

    private void ApplyTargetAvoidance()
    {
        // Nur wenn der Agent wirklich unterwegs ist und ein Target existiert
        //if (!agent.hasPath || agent.desiredVelocity.sqrMagnitude < 0.0001f || targetTransform == null)
        //    return;

        Vector3 origin = transform.position;
        Vector3 toTarget = targetTransform.position - origin;
        float distance = toTarget.magnitude;

        // Nur reagieren, wenn das Target im Target-Avoidance-Radius ist
        if (distance > targetAvoidanceOuterRadius || distance <= 0.0001f)
            return;

        // Normalisierte Richtung zum Target
        Vector3 toTargetDir = toTarget / distance;

        // Stabile Links/Rechts-Entscheidung pro Agent-Target-Paar
        int pairHash = this.GetInstanceID() ^ targetTransform.GetInstanceID();
        float sideSign = ((pairHash & 1) == 0) ? 1f : -1f; // konstant für dieses Paar

        // Seitliche Richtung relativ zur Linie Agent -> Target
        Vector3 sideDir = Vector3.Cross(Vector3.up, toTargetDir) * sideSign;

        // Basisrichtung: weg vom Target
        Vector3 awayDir = -toTargetDir;

        // Seitliche Komponente einmischen
        if (targetSideStepStrength > 0f && sideDir.sqrMagnitude > 0.0001f)
        {
            sideDir.Normalize();
            awayDir = (awayDir + sideDir * targetSideStepStrength).normalized;
        }

        // Avoidance-Stärke relativ zur Distanz
        float tBase = Mathf.Clamp01(
            (targetAvoidanceOuterRadius - distance) / (targetAvoidanceOuterRadius - targetAvoidanceInnerRadius)
        );

        float t = Mathf.Clamp01(tBase * targetAvoidanceStrength);

        // Basisbewegung: das, was der Agent sonst tun würde
        Vector3 baseVelocity =
            (agent.velocity.sqrMagnitude > 0.0001f)
                ? agent.velocity
                : agent.desiredVelocity;

        // Weglaufgeschwindigkeit vom Target
        Vector3 runAwayVelocity = awayDir * agent.speed * targetAvoidanceSpeedMultiplier * targetAvoidanceStrength;

        // Sanft zwischen normaler Bewegung und Ausweichbewegung mischen
        agent.velocity = Vector3.Lerp(baseVelocity, runAwayVelocity, t);
    }

    #endregion
    #region NavAreaMethods

    #endregion
    #endregion
    #region Reset
    private void Reset()
    {
        heardSoundintensity = 0f;
        #region DeathReset
        //isDead = false; those two are needed if i want to clean up the death state for respawns with objectpooling and similar
        //agentHealth = agentMaxHealth;those two are needed if i want to clean up the death state for respawns with objectpooling and similar
        #endregion
    }
    #endregion    
}


