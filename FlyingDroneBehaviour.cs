using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static FlyingDroneDetectionSystem;

public class FlyingDroneBehaviour : MonoBehaviour
{
    private float timer = 0f;
    private float interval = 1f;
    #region MovementReference
    [Header("Ziel")]
    public Transform target;
    [SerializeField] private Transform player;
    [SerializeField] public GameObject scanner;

    [Header("Bewegung")]
    public float maxSpeed = 12f;
    public float maxAcceleration = 30f;
    public float arriveRadius = 0.1f;
    public float slowRadius = 10f;
    [SerializeField] private float activeSuspicionSpeed;

    [Header("Hindernisvermeidung")]
    public float avoidRange = 6f;     // wie weit nach vorne geprüft wird
    public float avoidRadius = 0.6f;  // Radius des SphereCasts (ungefähr Drohnen-"Rumpfradius")
    public float avoidStrength = 20f; // Stärke der Ausweichlenkung
    public LayerMask obstacleMask ;    // Layer der Hindernisse

    [Header("Patrouille")]
    public bool usePatrolPoints = false;
    public Transform[] patrolpoints;
    public bool loopPatrolPoints = true;
    public float patrolpointReachRadius = 2f;

    private int _patrolpointIndex = 0;

    // Dynamisches Ziel (folgt einem Transform mit Offset)
    private bool _useDynamicTarget = true; // xcvb wenn ich diese bool auf false setze geht wahrscheinlich das meiste den bach runter :| 
    private Vector3 _dynamicTargetPos;
    private Coroutine _dynamicTargetRoutine;

    // Oben bei den Feldern (optional einstellbar)
    public float stopSpeed = 0.05f;      // unterhalb dieser Geschwindigkeit wird hart auf 0 gesetzt
    public float noAvoidBuffer = 0.5f;   // Abstandspuffer, in dem Avoidance aus ist

    [Header("Scheinwerfer (Suche)")]
    [SerializeField] private Transform searchLight; // Child-Objekt mit dem Scheinwerfer
    private Coroutine _searchlightRoutine;
    private Vector3 _lastSearchDirWorld; // Für Mindestabstand zwischen Zielen
    public bool searchConeUsesWorldDown = true; // true = Vector3.down, false = -transform.up
    private Coroutine _searchlightAimRoutine;

    // === Felder (oben ergänzen) ===
    [Header("Robuste Omnidirektionale Avoidance (immer an)")]
    public float reactionTime = 0.3f;          // Vorlaufzeit
    public float backstep = 0.5f;              // Startpunkt pro Cast leicht entgegen der Cast-Richtung
    public int omniSamples = 24;               // Richtungsanzahl (Fibonacci-Sphere)
    public float avoidNormalWeight = 1.0f;     // Gewichtung Normalschub
    public float avoidDirWeight = 0.35f;       // Gewichtung entlang -Richtung (weg vom Strahl)
    public float penetrationResolveStrength = 40f;
    public float avoidMemorySeconds = 0.2f;    // Verblassen
    public float minGroundClearance = 0f;      // 0 = aus; >0 erzwingt Mindesthöhe
    public LayerMask groundMask = ~0;               // optionaler Boden-Mask, falls Boden NICHT in obstacleMask ist

    private Vector3 _avoidMemory;
    private float _avoidMemoryTimer;
    private Vector3 _lastMoveDir = Vector3.up; // beliebiger Start, wird dynamisch überschrieben
    private Collider _selfCollider;

    #endregion

    #region HemiScanAndThickLosReference

    // ===================== Hemisphären-Scanner (Wellenfront, sparsam) =====================
    [Header("Hemisphären-Scanner (Wellenfront)")]
    [SerializeField] private Transform hemisphereCenter;      // Standard: Player
    [SerializeField] private Transform scanTarget;            // Ziel (typisch: Player)
    [SerializeField] private float hemisphereRadius = 50f;
    [SerializeField] private int hemispherePoints = 81;       // inkl. Top-Punkt
    [SerializeField] private float scanTickInterval = 0.03f;  // wie oft Rays abgefeuert werden
    [SerializeField] private int maxRaysPerTick = 8;          // Limit pro Tick
    [SerializeField] private float rescanCooldown = 0.5f;     // min. Zeit bis ein Punkt erneut geprüft wird
    [SerializeField] private float ringStepDeg = 8f;          // Ringbreite (Polarwinkel)
    [SerializeField] private LayerMask hemiSphereRaycastMask = ~0;      // Sichtstrahl-Maske
    [SerializeField] private string sightTag = "Player";      // Tag, den der Sichtstrahl treffen soll
    [SerializeField] private float stickTime = 0.35f;         // wie lange ein gefundener Punkt „klebt“, bevor neu gesucht wird
    [SerializeField] private float validStayExtraMargin = 0.0f; // zusätzlicher Puffer bei Aufenthaltsprüfung

    // Gizmo / Debug
    [Header("Scanner-Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawRays = true;
    [SerializeField] private int maxRayTrails = 200;
    [SerializeField] private float rayFadeSeconds = 1.2f;
    [SerializeField] private int gizmoEveryNth = 1; // nur jeden n-ten OnDawGizmos-Call zeichnen
    [SerializeField] private float gizmoPointSize = 0.01f; // relativ zum Radius

    private enum SampleState { Untested, Queued, TestedBlocked, TestedOK, Selected }

    private Vector3[] _hemiLocalDirs;    // feste, welt-orientierte Einheitsrichtungen (y>=0)
    private Vector3[] _hemiWorldPoints;  // center + dir * radius
    private SampleState[] _state;        // Status je Punkt
    private float[] _lastTestTime;       // Letzte Prüfzeit je Punkt
    private int[] _order;                // sortierte Indizes (Seed + Ringe)
    private int _cursor;                 // Fortschritt in _order
    private int _seedIndex = -1;
    private bool _scanActive;
    private Coroutine _scannerCo;
    private float _stickUntilTime = 0f;

    // Ray-Trail-Log (nur für Gizmos)
    private struct RayTrail { public Vector3 from, to; public float t0; public bool ok; }
    private System.Collections.Generic.List<RayTrail> _trails = new System.Collections.Generic.List<RayTrail>(256);

    // Reseed-Logik (Wann „neu vom Seed beginnen“?)
    [SerializeField] private float reseedAngleThresholdDeg = 8f;  // ab wieviel Grad Linienänderung neu starten
    [SerializeField] private float reseedMinInterval = 0.2f;      // nicht öfter als alle X s reseeden
    [SerializeField] private int reseedInnerRingsToReset = 2;   // wie viele innerste Ringe „kaltstellen“

    private Vector3 _lastLineA, _lastLineB;
    private float _nextReseedTime = 0f;

    // === Wave/Epoch (Single-Mode) ===
    private bool _waveActive = false;   // läuft gerade eine Testwelle?
    private int _epoch = 0;            // Wellen-/Suchdurchlaufzähler
    private int _waveCursor = 0;       // Fortschritt in _order während der aktiven Welle
    private int[] _testedEpoch;         // pro Punkt: in welcher Epoche zuletzt getestet

    [Header("LoS (dicker Strahl)")]
    [SerializeField] private bool useThickLoS = true;
    [SerializeField] private float losCapsuleRadius = 0.35f; // Tipp: ≈ max(0.3, avoidRadius*0.8f)
    [SerializeField] private float losBackstep = 0.12f;       // 5–20 cm vorm Ziel einkürzen

    // NonAlloc-Puffer, um GC zu vermeiden
    private readonly RaycastHit[] _losHits = new RaycastHit[16];

    private bool seesPlayer = false;
    private bool canFire = false;

    [Header("Debug LoS Capsule")]
    [SerializeField] private bool debugDrawLoSCapsule = true;
    [SerializeField] private float debugCapsulePersist = 0.15f;

    private Vector3 _dbgCapA, _dbgCapB;
    private float _dbgCapR;
    private bool _dbgCapOK;
    private float _dbgCapUntil;


    // ==== Reichweiten-Scan (NEU) ====
    [SerializeField] private float hemisphereMinRadius = 20f;
    [SerializeField] private float hemisphereMaxRadius = 50f;
    [SerializeField] private int hemisphereRadialSteps = 3; // Anzahl radialer Samples pro Richtung
    [SerializeField] private bool radialScanFarToNear = true; // erst weit, dann näher

    // Fallback für Altdaten im Inspector: nutzt altes 'hemisphereRadius' als Mid, wenn Min/Max nicht gesetzt
    private float HemisphereMidRadius =>
        (hemisphereMinRadius > 0f && hemisphereMaxRadius > 0f)
            ? 0.5f * (hemisphereMinRadius + hemisphereMaxRadius)
            : hemisphereRadius; // <- dein altes Feld bleibt bestehen

    [SerializeField] private bool adaptiveRayBudget = true;
    [SerializeField] private int raysPerTickMin = 8;
    [SerializeField] private int raysPerTickMax = 24;
    [SerializeField] private float budgetRampSeconds = 0.5f; // nach ~0.5s auf Max
    private float _sinceLastSelection = 0f;
    public struct SightAndChannelResult
    {
        public bool inCone;         // liegt Ziel im Sichtkegel?
        public bool hasLoS;         // „normale“ Sicht (dünn/eng)
        public bool hasFireChannel; // dicker Kanal (z. B. für Projektil/Beam)
    }

    private Rigidbody rb;

    [Header("Hemisphere-Scanner – Adoption")]
    [SerializeField] private float hemiAdoptionTolerance = 0.1f; // Distanz, die als "eingenommen" gilt
    [SerializeField] private bool _hemiHasAdoptedOnce = false;   // Inspector-Preview
    public bool HemiHasAdoptedOnce => _hemiHasAdoptedOnce;       // Read-only API
    public System.Action OnHemiFirstAdoption;                    // optionales Callback

    private bool _hemiAdoptionPending = false;                   // wartet auf tatsächliches Einnehmen
    private Vector3 _hemiCurrentGoal;

    [Header("Active Suspicion (Look-Hold)")]
    [SerializeField] private float suspicionLoSHoldSeconds = 0.5f; // x Sekunden "anschauen"
    private float _suspicionLoSTimer = 0f;
    #endregion

    #region ImportOfTargetDetectionLevel
    [SerializeField] private FlyingDroneDetectionSystem fDDetectionSystemScript; // xcv ... if (fDDetectionSystemScript.targetDetectionLevel == xxx){uws.} ich muss im switch den wert aus dem anderen script prüfen

    #endregion

    #region StateManagementReference

    [Header("StateManagement")]
    private AgentState agentState;
    [SerializeField] private AgentState initialAgentState;
    [SerializeField] private AgentState previousAgentState; // Stores last state
    [SerializeField] private AgentTask agentTask;

    #region TargetSuspicionReference
    private Vector3 droneReturnPosition;
    private bool suspicionPositionReached = true;
    private float satisfactionDistance = 5f;
    private float returnDistance = 1f;
    [SerializeField] public GameObject suspicionDummy;
    #endregion

    #region PatrolReference
    [Header("Patrol")]
    [SerializeField] private float patrolTickInterval = 8f;          // Y Sekunden
    [SerializeField, Range(0f, 100f)] private float elevatedPatrolChancePercent = 25f; // X %
    [SerializeField] public bool elevatedPatrol = false;            // wird per Tick evtl. gesetzt
    private float _patrolTickTimer = 0f;
    [SerializeField] private float elevatedDescendSpeed = 6f;
    [SerializeField] private float elevatedDescendTolerance = 0.05f;

    private bool _descendingFromElevated;
    private float _descendTargetY;
    
    [SerializeField] private bool randomizePatrolOrder = true;
    [SerializeField] private bool avoidImmediateRepeat = true;
    [SerializeField] private float patrolMovementSpeed = 5f;

    [Header("ProximityBiasToPlayer in Patrol")]
    [SerializeField] private bool useProximityBias = false;                  // Master-Toggle
    [SerializeField, Range(0f, 100f)] private float proximityBiasTriggerPercent = 30f; // X %
    [SerializeField, Min(1)] private int proximityTopCountY = 2;             // y
    [SerializeField, Min(1)] private int proximityNextCountZ = 2;            // z
    [SerializeField, Range(0f, 100f)] private float proximityChooseYPercentB = 70f; // B % (N = 100-B)
    [SerializeField] private bool proximityExcludeCurrent = true;            // aktuellen Wegpunkt auslassen

    #endregion

    #region PatrolReturnReference
    [Header("Patrol Return")]
    [SerializeField] private float patrolReturnSpeed = 6f;        // X Einheiten/Sekunde
    [SerializeField] private float patrolReturnTolerance = 0.5f;  // wann "angekommen"
    private bool _patrolReturnPending = false;                    // sind wir im Rückkehrflug?
    private bool _hasPatrolReturnPoint = false;                   // ist überhaupt was gemerkt?
    private Vector3 _patrolReturnPoint;                           // gemerkte Position
    [SerializeField] private float elevatedReturnYOffset = 0f; // Y-Offset für Returnpoint beim Verlassen von Elevated

    #endregion

    #region ElevatedPatrolReference
    [Header("Elevated Patrol")]
    [SerializeField] private float elevatedPatrolTickInterval = 8f;           // Y Sekunden
    [SerializeField, Range(0f, 100f)] private float deElevateChancePercent = 25f; // X %
    private float _elevatedPatrolTickTimer = 0f;
    [SerializeField] private float searchlightReenableDelay = 2f; // „x Sekunden“
    private Coroutine _searchlightReenableCo;
    [SerializeField] private float elevatedPatrolYOffset = 8f;
    public enum ElevatedPatrolStartMode { ContinueCurrent, RandomFromList }
    [SerializeField] private ElevatedPatrolStartMode elevatedPatrolStartMode = ElevatedPatrolStartMode.ContinueCurrent;
    [SerializeField] private float elevatedAscendSpeed = 6f;     // x Einheiten/Sek beim Hochfahren
    [SerializeField] private float elevatedAscendTolerance = 0.05f; // wann "angekommen" ist
    [SerializeField] private float elevatedPatrolCruiseSpeed = 3f;  // normale Patrouillier-Geschw.

    private bool _elevatedAscending;
    private float _elevatedTargetY;

    [SerializeField] private GameObject droneModel;
    [SerializeField] private GameObject droneEyeModel;
    [SerializeField] private ParticleSystem reappearParticles;

    // --- Elevation Audio ---
    [Header("Elevation Audio")]
    [SerializeField, Range(0f, 1f)] private float elevationAudioVolFrom = 0f;
    [SerializeField, Range(0f, 1f)] private float elevationAudioVolTo = 0.9f;
    [SerializeField] private float elevationAudioPitchFrom = 1.0f;      // optional
    [SerializeField] private float elevationAudioPitchTo = 1.1f;      // optional
    [SerializeField] private float elevationAudioRelease = 0.35f;     // Fade-Out in s

    private Coroutine _elevAudioFadeCo;

    // --- Elevation FX ---
    [Header("Elevation FX")]
    [SerializeField] private ParticleSystem elevationFxRoot; // Root-PS; alle Children werden mitgesteuert
    [SerializeField] private float elevationFxRateFrom = 5f; // X
    [SerializeField] private float elevationFxRateTo = 60f; // Y
    [SerializeField] private ParticleSystemStopBehavior elevationFxStopBehavior = ParticleSystemStopBehavior.StopEmitting;
    [SerializeField] private ParticleSystem droneDissapearanceParticles;


    private List<ParticleSystem> _elevationPs;  // gecachte Liste aller PS (Root + Kinder)
    private bool _elevFxActive = false;
    private float _elevatedStartY = 0f; // Start-Y für Progress
    #endregion

    #region StayStateReference
    [Header("StayState")]
    [SerializeField] private float stoppingDistanceStay = 0.1f;
    private Vector3 droneStayPosition;
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

    #region UniversalBehaviourAndActionMethdodReference
    private Vector3 lastDestination;
    #endregion

    // --- Scan-Tags (zentral) ---
    private const string TAG_PLAYER = "Player";
    private const string TAG_SUSPICION = "SuspicionDummy";

    #region EnumsFor: State, Task, Need
    private enum AgentState
    {
        None,
        Dead,
        Stay,
        Guard,
        Patrol,
        ElevatedPatrol,
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

    #endregion

    #endregion

    #region ColourChangeReference
    [SerializeField] private LightPalette lightPaletteScript;
    [SerializeField] private MaterialPaletteTarget meshMaterialPaletteScript;
    [SerializeField] private MaterialPaletteTarget particleMaterialPaletteScript;
    #endregion

    #region FireControl
    // === FX: feuert, wenn canFire == true ===
    [Header("FX")]
    [SerializeField] private ParticleSystem fireFX;
    [SerializeField] private ParticleSystemStopBehavior stopBehavior = ParticleSystemStopBehavior.StopEmitting;

    private bool _fxActive = false;
    private bool _fxSoundActive = false;
    #endregion

    #region SoundReference
    [Header("Sounds")]
    [SerializeField] private ExclusiveAudioControllerForStates stateAudioScript;
    [SerializeField] private AudioSource droneBaseSound;
    [SerializeField] private AudioSource droneDetectionSound;
    [SerializeField] private AudioSource droneSearchSound;
    [SerializeField] private AudioSource droneFireFXSound;
    [SerializeField] private AudioSource droneLightSoundActivate;
    [SerializeField] private AudioSource droneLightSoundDeactivate;
    [SerializeField] private AudioSource droneStealthAppearanceSoundCue;
    [SerializeField] private AudioSource droneStealthDisappearanceSoundCue;
    [SerializeField] private AudioSource elevationAudio;                 // Loop-Clip zuweisen

    #endregion

    #region ScannerReference
    [Header("Scanner")]
    private bool _scannerBaselineSet = false;
    private bool _scannerWasActive = false;
    #endregion
    private void Awake()
    {

        #region StateHandling
        agentState = initialAgentState;
        #endregion
        #region RigidBodyPreparation
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0.15f;
        #endregion
        _selfCollider = GetComponent<Collider>();
        if (target != null) _dynamicTargetPos = target.position;
        else _dynamicTargetPos = transform.position; // Fallback
        #region StayState
        droneStayPosition = transform.position;
        #endregion

        // Ray-/Capsule-Casts: nur diese Layer zulassen
        // (alles andere wird ignoriert)
        obstacleMask = LayerMask.GetMask("DroneAvoidanceArea");
        hemiSphereRaycastMask = LayerMask.GetMask("Player", "SuspicionDummy", "GroundAndWalls", "Default");

    }

    private void Start()
    {
        //SelectNearestWaypointAsCurrentTarget();
        // folgt dem Player alle 0.1 s, mit +10 in Y
        //SetPlayerAsTargetWithYOffset(playerTransform: player, updateIntervalSeconds: 0.1f, yOffset: 10f);
        //AimSearchlightAt(player, 150f, true);
        // Beispiel: 50 Einheiten Radius, 13 Punkte (odd!), 0.02s pro Schritt, 2 Rays je Schritt, 0.25 Overlap-Prüfradius
        //dummyPos = dummy.transform.position;
        //StartHemisphereScannerRange(player, 35f, 100f, "Player", 271, 0.05f);
        droneBaseSound.Play();
    }

    private void Update()
    {
        //CheckEffectiveRange();
        DoesHeSeeAndCanHeFire(); // ich sollte das durch einen reinen distanzcheck ersetzen und den los+thickness- check anderswo platzieren
        StateLogic();
        UpdateState();
        ActiveStateBehaviour();
        //Animations();
        //DeathCheck();
        //Debug.Log("agentState " + agentState);
        rb.angularVelocity = Vector3.zero;
        //Debug.Log("useThickLoS"+useThickLoS);
    }

    private void FixedUpdate()
    {
        MoveAndAvoid();

        //dummyPos = dummy.transform.position;
        //
        //if (PositionRespectsAvoidRadius(dummyPos, 3f, false))
        //{
        //    Debug.Log("true");
        //}
        //else
        //{
        //    Debug.Log("false");
        //}
        // Sicht auf den Spieler mit 35° Kegel, dicker Strahl (Capsule radius 0.15 m)
        //DoesHeSeeAndCanHeFire();
        //UpdateFireFX();
    }


    #region StateManagement
    private void StateLogic()
    {
        switch ((fDDetectionSystemScript.targetDetectionLevel, agentTask, canFire, elevatedPatrol)) // Tuple switching
        {
            case (TargetDetectionLevel.None, AgentTask.None, _, _):
                agentState = AgentState.None;
                break;

            case (TargetDetectionLevel.None, AgentTask.Stay, _, _):
                agentState = AgentState.Stay;
                break;

            case (TargetDetectionLevel.None, AgentTask.Patrol, _, false):
                agentState = AgentState.Patrol;
                break;

            case (TargetDetectionLevel.None, AgentTask.Patrol, _, true):
                agentState = AgentState.ElevatedPatrol;
                break;

            case (TargetDetectionLevel.PassiveSuspicion, _, _, _):
                agentState = AgentState.SuspectPassively;
                break;

            case (TargetDetectionLevel.ActiveSuspicion, _, _, _):
                agentState = AgentState.SuspectActively;
                break;

            case (TargetDetectionLevel.DetectionMemory, _, _, _):
                agentState = AgentState.ChaseTarget;
                break;

            case (TargetDetectionLevel.DetectingTarget, _, false, _):
                agentState = AgentState.ApproachTarget;
                break;

            case (TargetDetectionLevel.DetectingTarget, _, true, _):
                agentState = AgentState.AttackTarget;
                break;

            default:
                agentState = AgentState.None; // Fallback state
                break;
        }
    }

    private void UpdateState()
    {
        if (agentState != previousAgentState)
        {
            OnExitState(previousAgentState);   // ← erst altes State aufräumen
            OnEnterState(agentState);          // ← dann neues State setzen (deine Enter*State)
            previousAgentState = agentState;
        }
    }

    private void OnEnterState(AgentState newState)
    {
        switch (newState)
        {
            case AgentState.None: return;
            case AgentState.Stay: EnterStayState(); break;
            case AgentState.Patrol: EnterPatrolState(); break;
            case AgentState.ElevatedPatrol: EnterElevatedPatrolState(); break;
            case AgentState.SuspectPassively: EnterPassiveSuspicionState(); break;
            case AgentState.SuspectActively: EnterActiveSuspicionState(); break;
            case AgentState.ChaseTarget: EnterChaseTargetState(); break;
            case AgentState.ApproachTarget: EnterApproachTargetState(); break;
            case AgentState.AttackTarget: EnterAttackTargetState(); break;
        }
    }

    private void OnExitState(AgentState oldState)
    {
        switch (oldState)
        {
            case AgentState.None:return;
            case AgentState.Stay: ExitStayState(); break;
            case AgentState.Patrol: ExitPatrolState();break;
            case AgentState.ElevatedPatrol: ExitElevatedPatrolState();break;
            case AgentState.SuspectPassively:ExitPassiveSuspicionState(); break;
            case AgentState.SuspectActively:ExitActiveSuspicionState();break;
            case AgentState.ChaseTarget:ExitChaseTargetState(); break;
            case AgentState.ApproachTarget: ExitApproachTargetState();break;
            case AgentState.AttackTarget:  ExitAttackTargetState(); break;
        }

        // Gemeinsame Aufräumarbeiten, die für alle States gelten könnten, kommen hierhin.
        // Beispiel: optionale Scanner/FX abschalten, wenn du sie state-gebunden startest:
        // StopHemisphereScanner(); // nur, wenn du ihn stateweise aktivierst
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
            case AgentState.ElevatedPatrol:
                ElevatedPatrolState();
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

    #region ActiveStateMethods
    private void PatrolState()
    {
        // 1) Falls wir gerade aus Elevated absteigen, hat das Vorrang
        if (_descendingFromElevated)
        {
            _dynamicTargetPos.x = transform.position.x;
            _dynamicTargetPos.z = transform.position.z;
            _dynamicTargetPos.y = _descendTargetY;

            if (Mathf.Abs(transform.position.y - _descendTargetY) <= elevatedDescendTolerance)
            {
                _descendingFromElevated = false;
                _useDynamicTarget = false;
                usePatrolPoints = true;
                maxSpeed = patrolMovementSpeed;
            }
            return;
        }

        // 2) Rückkehr zur gemerkten Patrol-Position (nur wenn pending)
        if (_patrolReturnPending)
        {
            _useDynamicTarget = true;
            _dynamicTargetPos = _patrolReturnPoint;
            maxSpeed = patrolReturnSpeed;

            float dist = Vector3.Distance(transform.position, _patrolReturnPoint);
            if (dist <= patrolReturnTolerance)
            {
                // Ankunft → PatrolPoints aktivieren und normal weiter
                _patrolReturnPending = false;
                _useDynamicTarget = false;
                usePatrolPoints = true;
                maxSpeed = patrolMovementSpeed;
                //Debug.Log("HasReturned");
                // Optional: am nächsten Wegpunkt weitermachen
                // SelectNearestWaypointAsCurrentTarget();
            }
            return; // solange wir zurückkehren, keine normale Patrol-Logik
        }

        // 3) reguläre Patrol-Logik
        PatrolTickUpdate();
    }



    private void ElevatedPatrolState()
    {        

        if (_elevatedAscending)
        {
            // Ziel vertikal über uns halten (falls XZ leicht driftet)
            _dynamicTargetPos.x = transform.position.x;
            _dynamicTargetPos.z = transform.position.z;
            _dynamicTargetPos.y = _elevatedTargetY;

            // Progress 0..1 anhand tatsächlicher Höhe (robust gegen Speedänderungen)
            float ascProgress = Mathf.InverseLerp(_elevatedStartY, _elevatedTargetY, transform.position.y);
            UpdateElevationFX(ascProgress);
            UpdateElevationAudio(ascProgress);


            if (Mathf.Abs(transform.position.y - _elevatedTargetY) <= elevatedAscendTolerance)
            {
                // Höhe erreicht → auf "normale" Elevated-Patrol umschalten
                _elevatedAscending = false;

                _useDynamicTarget = false;   // zurück auf Wegpunkte …
                usePatrolPoints = true;    // … die dank Offset angeflogen werden
                maxSpeed = elevatedPatrolCruiseSpeed;

                droneModel.SetActive(false);
                droneEyeModel.SetActive(false);
                StopElevationFX(false); // StopEmitting (bestehende Partikel dürfen ausklingen)
                droneDissapearanceParticles.Play();
                droneStealthDisappearanceSoundCue.Play();
                droneBaseSound.Stop();
                StopElevationAudio(false); // weich ausblenden
                // Scheinwerfer re-enablen machst du weiter mit deiner bestehenden Delay-Logik.
                // (Hier absichtlich nichts starten.)
            }
            return; // solange Ascend aktiv ist, NICHT die reguläre Patrol-Logik laufen lassen
        }
        ElevatedPatrolTickUpdate();
        // Ab hier läuft deine normale Elevated-Patrol.
        // Wichtig: In TryGetActiveTargetPosition den Y-Offset addieren,
        // z.B. targetPos += Vector3.up * elevatedPatrolYOffset, wenn agentState == ElevatedPatrol.
    }

    private void StayState()
    {

    }

    private void PassiveSuspicionState()
    {

    }

    private void ActiveSuspicionState()
    {
        // Phase 1: Beobachten, bis x Sekunden am Stück Sicht besteht
        if (!suspicionPositionReached)
        {
            bool looking = HasSightTo(suspicionDummy.transform);
            _suspicionLoSTimer = looking
                ? _suspicionLoSTimer + Time.deltaTime
                : 0f;

            if (_suspicionLoSTimer >= suspicionLoSHoldSeconds)
            {
                // „Need“ erfüllt → Rückkehrphase starten
                suspicionPositionReached = true;
                StopHemisphereScanner();          // Scanner aus, wir kehren heim
                DroneSetDestinationToReturnPosition();
            }

            return; // Movement/Scanner erledigen den Rest bis Hold erfüllt
        }

        // Phase 2: Zurück zur Return-Position, dann Need zurücksetzen
        float dist = Vector3.Distance(transform.position, droneReturnPosition);
        if (dist > returnDistance)
        {
            maxSpeed = activeSuspicionSpeed;
            DroneSetDestinationToReturnPosition();
            return;
        }

        // Rückkehr abgeschlossen → ActiveSuspicion-Need ist erfüllt
        fDDetectionSystemScript.agentNeed = FlyingDroneDetectionSystem.Need.None;
    }


    private void ChaseTargetState()
    {
        //PeriodicCallout(); //xcvb
        UpdateFireFX();
    }

    private void ApproachTargetState()
    {
        //PeriodicCallout(); //xcvb
        UpdateFireFX();
    }

    private void AttackTargetState()
    {
        //PeriodicCallout(); //xcvb
        UpdateFireFX();
    }

    #endregion


    //___________________________________________________________________


    #region EnterStateMethods
    private void EnterStayState()
    {
        SetEyeMeshLightAndParticleColors(0);
        maxSpeed = 1f;
        DroneSetDestinationToStayPosition();
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        StartSearchlightSweep();
        //Debug.Log("Drone is now staying.");
    }

    private void EnterPatrolState()
    {
        SetEyeMeshLightAndParticleColors(0);
        _patrolTickTimer = 0f;

        if (previousAgentState == AgentState.ElevatedPatrol)
        {
            // Erst absenken, Waypoints pausieren
            BeginDescentFromElevated();

            // Suchscheinwerfer NICHT sofort – wie von dir gewünscht verzögert wieder an
            TriggerSearchlightEnableAfterDelay(searchlightReenableDelay);
            return;
        }

        // Normalfall: aus einem anderen State zurück in Patrol
        // → Falls wir eine Rückkehr-Position haben und noch nicht praktisch dort sind:
        if (_hasPatrolReturnPoint &&
            (transform.position - _patrolReturnPoint).sqrMagnitude > patrolReturnTolerance * patrolReturnTolerance)
        {
            _patrolReturnPending = true;
            _useDynamicTarget = true;
            usePatrolPoints = false;            // <- wichtig: NICHT sofort aktivieren
            _dynamicTargetPos = _patrolReturnPoint;
            maxSpeed = patrolReturnSpeed;
            //Debug.Log("StartsReturn");
            StartSearchlightSweep();            // Scheinwerfer darf schon arbeiten
            return;
        }

        // Andernfalls direkt PatrolPoints fortsetzen
        _patrolReturnPending = false;
        _useDynamicTarget = false;
        usePatrolPoints = true;
        maxSpeed = patrolMovementSpeed;
        StartSearchlightSweep();

    }


    private void EnterElevatedPatrolState()
    {
        SetEyeMeshLightAndParticleColors(0);
        _elevatedPatrolTickTimer = 0f;   // Zählung beginnt beim Enter
        scanner.SetActive(false);
        CheckScannerToggleOnce();
        droneLightSoundDeactivate.Play();
        _elevatedStartY = transform.position.y; // hast du schon für FX
        StartElevationAudio();

        maxSpeed = 30f;
        _useDynamicTarget = false;

        // Beim Enter ggf. zufälligen Startpunkt wählen
        if (patrolpoints != null && patrolpoints.Length > 0 &&
            elevatedPatrolStartMode == ElevatedPatrolStartMode.RandomFromList)
        {
            _patrolpointIndex = Random.Range(0, patrolpoints.Length);
        }

        // Ascend-Phase vorbereiten
        _elevatedTargetY = transform.position.y + elevatedPatrolYOffset;
        _elevatedAscending = true;

        _elevatedStartY = transform.position.y; // Progress-Referenz
        StartElevationFX();

        _useDynamicTarget = true;    // wir steuern zunächst eine reine Höhenposition an
        usePatrolPoints = false;   // Waypoints pausieren, bis Höhe erreicht
        maxSpeed = elevatedAscendSpeed;

        // Ziel direkt über der aktuellen XZ-Position:
        _dynamicTargetPos = new Vector3(transform.position.x, _elevatedTargetY, transform.position.z);
        // Falls du im ElevatedModus das Searchlight aus lassen willst, NICHT hier starten
        // (du hast bereits Logik zum verzögerten Re-Enable nach Exit).
        StopAimingSearchlight();
        StopSearchlightSweep();

    }

    private void EnterPassiveSuspicionState()
    {
        // --- Transition-spezifischer Einmal-Check pro Eintritt ---
        if (previousAgentState == AgentState.SuspectActively)
        {
            stateAudioScript.PlayExclusiveAudio(3);
        }
        else
        {
            stateAudioScript.PlayExclusiveAudio(0);
        }
        usePatrolPoints = true;
        SetEyeMeshLightAndParticleColors(1);
        maxSpeed = 3f;
        ActivateSuspicionDummy();
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        AimSearchlightAt(suspicionDummy.transform, 150f, true);
        //DroneSetReturnPositionToSelf();
        //DroneSetDestinationToSelf();
    }

    private void EnterActiveSuspicionState()
    {
        droneSearchSound.Play();
        stateAudioScript.PlayExclusiveAudio(1);
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        SetEyeMeshLightAndParticleColors(2);
        maxSpeed = 15f;
        ActivateSuspicionDummy();

        _useDynamicTarget = true;
        usePatrolPoints = false;

        suspicionPositionReached = false;   // wird nun durch LoS-Hold erfüllt
        _suspicionLoSTimer = 0f;

        AimSearchlightAt(suspicionDummy.transform, 150f, true);
        DroneSetReturnPositionToSelf();

        // KEIN Adoption-Reset, KEIN Event-Subscribe

        // „Normaler“ Scanner, aber Target = Dummy
        StartHemisphereScannerRange(
            centerAndTarget: suspicionDummy.transform,
            minRadius: 35f,
            maxRadius: 100f,
            tagToCheck: TAG_SUSPICION,
            points: 271,
            tickInterval: 0.05f
        );

    }

    private void EnterChaseTargetState()
    {
        SetEyeMeshLightAndParticleColors(3);
        maxSpeed = 20f;
        //stateAudioScript.PlayExclusiveAudio(2);
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        AimSearchlightAt(player, 150f, true);
        StartHemisphereScannerRange(player, 35f, 100f, TAG_PLAYER, 271, 0.05f);
    }

    private void EnterApproachTargetState()
    {
        SetEyeMeshLightAndParticleColors(3);
        maxSpeed = 20f;
        //stateAudioScript.PlayExclusiveAudio(2);
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        AimSearchlightAt(player, 150f, true);
        StartHemisphereScannerRange(player, 35f, 100f, TAG_PLAYER, 271, 0.05f);
    }

    private void EnterAttackTargetState()
    {
        SetEyeMeshLightAndParticleColors(3);
        maxSpeed = 20f;
        //stateAudioScript.PlayExclusiveAudio(2);
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        AimSearchlightAt(player, 150f, true);
        StartHemisphereScannerRange(player, 35f, 100f, TAG_PLAYER, 271, 0.05f);
    }
    #endregion


    //________________________________________________________


    #region ExitStateMethods
    private void ExitStayState()
    {
        StopSearchlightSweep();
        return;
    }

    private void ExitPatrolState()
    {
        // Position merken, von der wir später wieder starten wollen
        // ALT:
        // _patrolReturnPoint = transform.position;
        // Debug.Log("ReturnPosPlaced");
        // _hasPatrolReturnPoint = true;

        // NEU:
        bool goingToElevated = (agentState == AgentState.ElevatedPatrol);
        if (!goingToElevated && !_descendingFromElevated)
        {
            TryEstablishReturnPoint(transform.position, 0f);
        }


        StopSearchlightSweep();
        CancelPendingSearchlightEnable();   // <— NEU: Timer abbrechen
        _descendingFromElevated = false;    // <— NEU: Abstieg sicher beenden

        _useDynamicTarget = true;
        usePatrolPoints = false;
        _patrolTickTimer = 0f;            // Zählung beim Exit zurücksetzen
        return;
    }

    private void ExitElevatedPatrolState()
    {
        StopElevationFX(false);
        droneModel.SetActive(true);
        droneEyeModel.SetActive(true);
        reappearParticles.Play();
        droneStealthAppearanceSoundCue.Play();
        droneBaseSound.Play();
        StopElevationAudio(false);
        _elevatedAscending = false; // falls wir während des Aufstiegs abgebrochen wurden

        elevatedPatrol = false;
        _elevatedPatrolTickTimer = 0f;   // beim Exit zurücksetzen
                                         // optional: Audio/FX zurücksetzen
                                         // Aufstieg als abgeschlossen gilt, wenn _elevatedAscending bereits beendet wurde.
        if (!_elevatedAscending) // d.h. wir waren im Cruise, nicht mehr am Hochfahren
        {
            // Während Abstieg wird hier noch nicht sein; zur Sicherheit blockt der Helper ohnehin.
            TryEstablishReturnPoint(transform.position, elevatedReturnYOffset);
        }
    }

    private void ExitPassiveSuspicionState()
    {
        DeactivateSuspicionDummy();
        StopAimingSearchlight();
        usePatrolPoints = true;
        return;
    }

    private void ExitActiveSuspicionState()
    {
        droneSearchSound.Stop();
        DeactivateSuspicionDummy();
        StopHemisphereScanner();
        StopAimingSearchlight();
        _suspicionLoSTimer = 0f;
        suspicionPositionReached = false;
    }


    private void ExitChaseTargetState()
    {
        StopAimingSearchlight();
        StopHemisphereScanner();
        return;
    }

    private void ExitApproachTargetState()
    {
        StopAimingSearchlight();
        StopHemisphereScanner();
        return;
    }

    private void ExitAttackTargetState()
    {
        StopAimingSearchlight();
        StopHemisphereScanner();
        return;
    }
    #endregion


    //______________________________________________________


    #region StateHelperMethods

    private void PatrolTickUpdate()
    {
        _patrolTickTimer += Time.deltaTime;
        if (_patrolTickTimer < patrolTickInterval) return;

        // Tick ausgelöst
        _patrolTickTimer -= patrolTickInterval; // Overflow behalten (stabil bei Frame-Jitter)

        if (!elevatedPatrol) // nur schalten, wenn noch nicht erhöht
        {
            // X% Chance
            if (Random.value * 100f < elevatedPatrolChancePercent)
            {
                elevatedPatrol = true;
                //Debug.Log("elevatedPatrolActivated");
                // optional: Event/Audio/Debug hier
                // OnPatrolElevated?.Invoke();
            }
        }
    }
    private void ElevatedPatrolTickUpdate()
    {
        _elevatedPatrolTickTimer += Time.deltaTime;
        if (_elevatedPatrolTickTimer < elevatedPatrolTickInterval) return;

        // Tick ausgelöst
        _elevatedPatrolTickTimer -= elevatedPatrolTickInterval; // overflow-freundlich

        // X% Chance, wieder zu normaler Patrol zurückzuschalten
        if (Random.value * 100f < deElevateChancePercent)
        {
            elevatedPatrol = false;
            //Debug.Log("elevatedPatrolDeActivated");
            // optional: Event/Audio/Debug hier
            // OnPatrolElevated?.Invoke();
        }
    }

    private void DroneSetDestinationToStayPosition()
    {
        _dynamicTargetPos = droneStayPosition;
    }

    private void DroneSetDestinationToSelf()
    {
        _dynamicTargetPos = transform.position;
    }

    private void DroneSetReturnPositionToSelf()
    {
        droneReturnPosition = transform.position;
    }
    private void DroneSetDestinationToReturnPosition()
    {
        _dynamicTargetPos = droneReturnPosition;
    }

    public void SetSuspicionPositionFromDetectionManager()
    {
        // im moment nicht im einsatz 
    }

    public void ActivateSuspicionDummy()
    {
        suspicionDummy.SetActive(true);
    }

    public void DeactivateSuspicionDummy()
    {
        suspicionDummy.SetActive(false);
    }

    public void SetSuspicionDummyPositionAtPlayerPosition()
    {
        suspicionDummy.transform.position = player.transform.position;
    }
    #endregion



    //__________StateMethodsAndStateManagementMethodsAbove_________



    #region MovementAndAvoidanceAndPatrol

    private void MoveAndAvoid()
    {
        // 0) Aktive Zielposition holen
        if (!TryGetActiveTargetPosition(out Vector3 targetPos))
            return;

        // 1) ARRIVE-Logik 
        Vector3 toTarget = targetPos - transform.position;
        float distance = toTarget.magnitude;

        float desiredSpeed;
        if (distance <= arriveRadius)
        {
            desiredSpeed = 0f; // am Ziel anhalten
        }
        else if (distance < slowRadius)
        {
            float t = (distance - arriveRadius) / Mathf.Max(0.0001f, slowRadius - arriveRadius);
            desiredSpeed = Mathf.Lerp(0f, maxSpeed, t);
        }
        else
        {
            desiredSpeed = maxSpeed; // weit weg -> volle Geschwindigkeit
        }

        Vector3 desiredVelocity = (distance > 0.001f) ? toTarget.normalized * desiredSpeed : Vector3.zero;

        // Harte Stop-Logik: Wenn wir eigentlich stehen sollen und fast stehen, setze exakt 0 und beende.
        if (desiredSpeed == 0f)
        {
            if (rb.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed)
            {
                rb.linearVelocity = Vector3.zero;
                return; // vermeidet, dass Avoidance noch schiebt
            }
        }

        // 2) OMNIDIREKTIONALE AVOIDANCE (immer aktiv, kein "vorne/hinten")
        Vector3 refDir = GetReferenceMoveDir(desiredVelocity);
        Vector3 avoid = ComputeAvoidanceOmni(refDir, desiredVelocity);

        // 3) STEERING anwenden
        Vector3 steering = (desiredVelocity - rb.linearVelocity) + avoid;
        steering = Vector3.ClampMagnitude(steering, maxAcceleration);
        rb.AddForce(steering, ForceMode.Acceleration);

        // --- Adoption-Check: Hat die Drohne einen Hemi-Punkt tatsächlich eingenommen? ---
        if (_scanActive && _useDynamicTarget && _hemiAdoptionPending)
        {
            float d2 = (transform.position - _hemiCurrentGoal).sqrMagnitude;
            if (d2 <= hemiAdoptionTolerance * hemiAdoptionTolerance)
            {
                _hemiAdoptionPending = false;
                if (!_hemiHasAdoptedOnce)
                {
                    _hemiHasAdoptedOnce = true;
                    OnHemiFirstAdoption?.Invoke(); // optionales One-shot-Event
                }
            }
        }

    }

    /// <summary>
    /// Liefert die aktuelle Zielposition:
    /// - Wenn usePatrolPoints true ist und es PatrolPoints gibt: aktuelle Waypoint-Position.
    ///   Beim Erreichen wird automatisch zum nächsten Waypoint gewechselt.
    /// - Sonst: target.position (falls vorhanden).
    /// Gibt false zurück, wenn weder PatrolPoints noch target verfügbar sind.
    /// </summary>
    /// <summary>
    /// Ermittelt die aktuelle Zielposition. Nutzt PatrolPoints, wenn usePatrolPoints aktiv ist.
    /// Wechsel zum nächsten Waypoint erfolgt automatisch, sobald die Drohne nahe genug ist.
    /// </summary>
    private bool TryGetActiveTargetPosition(out Vector3 targetPos)
    {
        if (_useDynamicTarget)
        {
            targetPos = _dynamicTargetPos;
            return true;
        }

        // Waypoint-Modus
        if (usePatrolPoints && patrolpoints != null && patrolpoints.Length > 0)
        {
            _patrolpointIndex = Mathf.Clamp(_patrolpointIndex, 0, patrolpoints.Length - 1);
            Transform wp = patrolpoints[_patrolpointIndex];
            if (wp == null)
            {
                targetPos = default;
                return false;
            }

            // Basis: Waypoint-Position
            targetPos = wp.position;

            // >>> Nur im ElevatedPatrol-State nach oben versetzen
            if (agentState == AgentState.ElevatedPatrol)
                targetPos += Vector3.up * elevatedPatrolYOffset;

            // Wechselradius und Auto-Advance
            float switchRadius = Mathf.Max(patrolpointReachRadius, arriveRadius);
            float dist = Vector3.Distance(transform.position, targetPos);

            if (dist <= switchRadius)
            {
                bool picked = false;

                // --- Proximity-Bias mit X% Chance ---
                if (useProximityBias && player != null && (Random.value * 100f) < proximityBiasTriggerPercent)
                {
                    int nextByProximity = PickProximityBiasedWaypoint(_patrolpointIndex, player.position);
                    if (nextByProximity >= 0)
                    {
                        _patrolpointIndex = nextByProximity;
                        picked = true;
                    }
                }

                // --- Fallback: RANDOM oder sequenziell (abhängig vom Toggle) ---
                if (!picked)
                {
                    if (randomizePatrolOrder)
                    {
                        int next = PickRandomWaypointIndexExcluding(_patrolpointIndex, avoidImmediateRepeat);
                        if (next >= 0) _patrolpointIndex = next;
                    }
                    else
                    {
                        _patrolpointIndex++;
                        if (_patrolpointIndex >= patrolpoints.Length)
                            _patrolpointIndex = loopPatrolPoints ? 0 : Mathf.Clamp(patrolpoints.Length - 1, 0, patrolpoints.Length - 1);
                    }
                }
            }




            return true;
        }

        // Fallback: normales target
        if (target != null)
        {
            targetPos = target.position;
            return true;
        }

        targetPos = default;
        return false;
    }


    #region MovementHelpers

    private Vector3 GetReferenceMoveDir(Vector3 desiredVel)
    {
        // Wähle „Bezugsrichtung“ ohne Forward-Annahme:
        Vector3 dir = Vector3.zero;
        float vMag = rb.linearVelocity.magnitude;
        if (vMag > 0.1f) dir = rb.linearVelocity / vMag;
        else if (desiredVel.sqrMagnitude > 1e-6f) dir = desiredVel.normalized;
        else dir = (_lastMoveDir.sqrMagnitude > 1e-6f ? _lastMoveDir.normalized : Vector3.up);

        _lastMoveDir = dir;
        return dir;
    }

    private Vector3 ComputeAvoidanceOmni(Vector3 refDir, Vector3 desiredVel)
    {
        // 1) Dynamischer Look-Ahead: Reaktionsweg + Bremsweg
        float speed = rb.linearVelocity.magnitude;
        float lookAhead = Mathf.Max(
            avoidRange,
            speed * reactionTime + (speed * speed) / Mathf.Max(0.01f, 2f * maxAcceleration)
        );

        // 2) Falls bereits Penetration: deterministisch herausdrücken
        Vector3 penetrationPush = ResolvePenetrationVector();
        Vector3 avoid = penetrationPush;

        // 3) Omnidirektionale Fan-Casts (Fibonacci-Kugel)
        //    Wir gewichten Treffer nach Nähe und kombinieren Normalen- und Richtungs-Abstoß.
        var dirs = FibonacciSphere(omniSamples, refDir); // gleichmäßig, aber grob auf refDir ausgerichtet
        Vector3 bestOpenDir = Vector3.zero;
        float bestOpenScore = -1f;

        Vector3 originBase = transform.position;

        foreach (var dir in dirs)
        {
            // Backstep pro Richtung, damit der Cast nicht exakt auf der Oberfläche startet
            Vector3 origin = originBase - dir * backstep;

            if (Physics.SphereCast(origin, avoidRadius, dir, out RaycastHit hit, lookAhead, obstacleMask, QueryTriggerInteraction.Collide)) // xcvb should by logic be set to QueryTriggerInteraction.Collide
            {
                // Nähe 0..1 (1 = sehr nah)
                float proximity = 1f - Mathf.Clamp01(hit.distance / Mathf.Max(0.0001f, lookAhead));
                float strength = avoidStrength * (0.5f + 0.5f * proximity);

                // Kombiniere Normale und Gegenrichtung (weg vom Strahl) – omnidirektional
                Vector3 awayNormal = hit.normal * avoidNormalWeight;
                Vector3 awayRayDir = (-dir) * avoidDirWeight;

                avoid += (awayNormal + awayRayDir) * strength;
            }
            else
            {
                // „Offene“ Richtung tracken (viel Platz)
                float score = 1f; // kein Hit = maximal offen
                if (score > bestOpenScore)
                {
                    bestOpenScore = score;
                    bestOpenDir = dir;
                }
            }
        }

        // 4) Leichte Ziel-Bias: wenn es eine offene Richtung gibt, nudge in gewünschte Richtung
        if (bestOpenScore > 0f && desiredVel.sqrMagnitude > 1e-6f)
        {
            Vector3 desiredDir = desiredVel.normalized;
            float align = Mathf.Clamp01(Vector3.Dot(bestOpenDir, desiredDir)); // 0..1
                                                                               // kleiner, sanfter Bias in Richtung „offene Richtung ∧ gewünschte Richtung“
            avoid += bestOpenDir * (avoidStrength * 0.1f * align);
        }

        // 5) Bodenfreiheit erzwingen (optional, ohne Boden als Hindernis zu führen) // xcv  GroundClearancePush ist not needed if ground is engulfed in obstacle mask collider
        //if (minGroundClearance > 0f && groundMask != 0)
        //{
        //    Vector3 gc = GroundClearancePush(minGroundClearance, lookAhead);
        //    avoid += gc * avoidStrength;
        //}

        // 6) Memory/Glättung: verhindert Flackern, wenn 1 Frame lang kein Hit kommt
        float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;
        if (avoid.sqrMagnitude > 1e-6f)
        {
            _avoidMemory = avoid;
            _avoidMemoryTimer = avoidMemorySeconds;
        }
        else if (_avoidMemoryTimer > 0f)
        {
            float t = Mathf.Clamp01(_avoidMemoryTimer / avoidMemorySeconds);
            avoid += _avoidMemory * t;
            _avoidMemoryTimer -= dt;
        }

        return avoid;
    }

    private Vector3 ResolvePenetrationVector()
    {
        if (_selfCollider == null) return Vector3.zero;

        // Kleine Überlappungssuche um Eigenradius
        float r = Mathf.Max(avoidRadius * 0.95f, 0.05f);
        var hits = Physics.OverlapSphere(transform.position, r, obstacleMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return Vector3.zero;

        Vector3 push = Vector3.zero;
        foreach (var other in hits)
        {
            if (other == null || other == _selfCollider) continue;

            if (Physics.ComputePenetration(
                _selfCollider, transform.position, transform.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 dir, out float dist))
            {
                // Summe aller Ausstoßvektoren
                push += dir * dist;
            }
        }

        if (push.sqrMagnitude > 1e-6f)
        {
            push = push.normalized * Mathf.Min(push.magnitude * penetrationResolveStrength, maxAcceleration);
        }

        return push;
    }

    // Erzwingt Mindesthöhe über Boden, wenn Boden nicht in obstacleMask ist.
    private Vector3 GroundClearancePush(float minClearance, float probeRange)
    {
        Vector3 p = transform.position;
        if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, probeRange, obstacleMask, QueryTriggerInteraction.Collide))
        {
            float h = hit.distance;
            if (h < minClearance)
            {
                float deficit = (minClearance - h) / Mathf.Max(minClearance, 0.0001f); // 0..1
                return Vector3.up * deficit; // skaliert; in ComputeAvoidanceOmni mit avoidStrength gewichtet
            }
        }
        return Vector3.zero;
    }
    #endregion

    #region PatrolHelpers

    /// <summary>
    /// Sucht den nächstgelegenen Waypoint im gegebenen Radius ab der aktuellen Position.
    /// Setzt ihn als aktuelles Ziel:
    /// - Bei usePatrolPoints = true: setzt _patrolpointIndex auf diesen Waypoint.
    /// - Bei usePatrolPoints = false: schreibt ihn in das normale 'target'.
    /// Gibt true zurück, wenn einer gefunden wurde.
    /// </summary>
    public bool SelectNearestWaypointAsCurrentTarget(float searchRadius = 100f)
    {
        if (patrolpoints == null || patrolpoints.Length == 0)
            return false;

        float bestSqr = searchRadius * searchRadius;
        int bestIdx = -1;

        Vector3 p = transform.position;
        for (int i = 0; i < patrolpoints.Length; i++)
        {
            Transform wp = patrolpoints[i];
            if (wp == null) continue;

            float sqr = (wp.position - p).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return false;

        if (usePatrolPoints)
        {
            _patrolpointIndex = bestIdx;
        }
        else
        {
            target = patrolpoints[bestIdx];
        }
        return true;
    }


    /// <summary>
    /// startet die Patrouille
    /// am nächstgelegenen Waypoint (im Suchradius). Falls keiner im Radius,
    /// wird als Fallback der global nächstgelegene gewählt.
    /// </summary>
    public bool ResumePatrolFromNearest(float searchRadius = 100f)
    {
        // 1) Jagd/Follow-Modi sauber beenden
        //StopFollowingDynamicTarget();   // falls aktiv
        // Optional: StopWaiting();     // falls du die Wait-Funktion nutzt
        // Optional: StopAimingSearchlight(); // wenn Scheinwerfer folgt, aber nicht nötig

        // 2) Waypoint-Modus aktivieren
        usePatrolPoints = true;
        target = null;

        // 3) Nächstgelegenen Wegpunkt wählen (im Radius) ...
        if (SelectNearestWaypointAsCurrentTarget(searchRadius))
            return true;

        // ... Fallback: global nächstgelegener (keine Radiusbegrenzung)
        return SelectNearestWaypointAsCurrentTarget(Mathf.Infinity);
    }

    private void BeginDescentFromElevated()
    {
        _descendTargetY = transform.position.y - elevatedPatrolYOffset;
        _descendingFromElevated = true;

        _useDynamicTarget = true;     // wir steuern zunächst nur die Y-Position an
        usePatrolPoints = false;    // Waypoints pausieren, bis unten
        maxSpeed = elevatedDescendSpeed;

        _dynamicTargetPos = new Vector3(transform.position.x, _descendTargetY, transform.position.z);
    }

    /// <summary>
    /// Versucht einen Patrol-Returnpoint zu setzen.
    /// Verweigert während Aufstieg (_elevatedAscending) oder Abstieg (_descendingFromElevated).
    /// yOffset wird auf die Y-Koordinate addiert.
    /// </summary>
    private bool TryEstablishReturnPoint(Vector3 basePos, float yOffset = 0f)
    {
        // Während Auf-/Abstieg keine Return-Points setzen
        if (_elevatedAscending || _descendingFromElevated)
            return false;

        _patrolReturnPoint = new Vector3(basePos.x, basePos.y + yOffset, basePos.z);
        _hasPatrolReturnPoint = true;
        //Debug.Log($"ReturnPosPlaced @{_patrolReturnPoint}");
        return true;
    }

    private int PickRandomWaypointIndexExcluding(int excludeIdx, bool avoidRepeat)
    {
        if (patrolpoints == null || patrolpoints.Length == 0) return -1;

        // valide (nicht-null) Indizes sammeln
        List<int> valid = new List<int>(patrolpoints.Length);
        for (int i = 0; i < patrolpoints.Length; i++)
        {
            if (patrolpoints[i] != null && (!avoidRepeat || i != excludeIdx))
                valid.Add(i);
        }

        // Fallback: wenn Vermeidung eine leere Menge ergibt (z. B. nur 1 Punkt), Wiederholung erlauben
        if (valid.Count == 0)
        {
            for (int i = 0; i < patrolpoints.Length; i++)
                if (patrolpoints[i] != null) valid.Add(i);
        }

        if (valid.Count == 0) return -1; // alles null

        int pick = Random.Range(0, valid.Count);
        return valid[pick];
    }

    private int PickProximityBiasedWaypoint(int currentIdx, Vector3 playerPos)
    {
        if (patrolpoints == null || patrolpoints.Length == 0) return -1;

        // 1) Alle gültigen Indizes sammeln (inkl. currentIdx!)
        List<int> all = new List<int>(patrolpoints.Length);
        for (int i = 0; i < patrolpoints.Length; i++)
            if (patrolpoints[i] != null) all.Add(i);
        if (all.Count == 0) return -1;

        // 2) Nach Distanz zum Spieler sortieren (nächster zuerst)
        all.Sort((a, b) =>
        {
            float da = (patrolpoints[a].position - playerPos).sqrMagnitude;
            float db = (patrolpoints[b].position - playerPos).sqrMagnitude;
            return da.CompareTo(db);
        });

        // 3) Y- und Z-Pools anhand der vollen Rangliste bilden
        int yCount = Mathf.Clamp(proximityTopCountY, 0, all.Count);
        int zCount = Mathf.Clamp(proximityNextCountZ, 0, Mathf.Max(0, all.Count - yCount));

        List<int> poolY = new List<int>(yCount);
        for (int i = 0; i < yCount; i++) poolY.Add(all[i]);

        List<int> poolZ = new List<int>(zCount);
        for (int i = yCount; i < yCount + zCount; i++) poolZ.Add(all[i]);

        // 4) Erst jetzt optional den aktuellen entfernen – ohne nachzufüllen
        if (proximityExcludeCurrent)
        {
            poolY.Remove(currentIdx);
            poolZ.Remove(currentIdx);
        }

        // 5) Pool wählen: B% = Y, (100-B)% = Z; bei leerem Pool auf den anderen ausweichen
        bool chooseY = (Random.value * 100f) < Mathf.Clamp(proximityChooseYPercentB, 0f, 100f);
        List<int> chosen = chooseY ? poolY : poolZ;
        if (chosen.Count == 0) chosen = chooseY ? poolZ : poolY;
        if (chosen.Count == 0) return -1; // nichts übrig

        // 6) Zufälliges Element aus dem gewählten Pool
        return chosen[Random.Range(0, chosen.Count)];
    }


    #endregion

    #region ElevationEffecthelpers
    private void StartElevationFX()
    {
        if (!elevationFxRoot) return;

        // beim ersten Mal einsammeln
        if (_elevationPs == null)
            _elevationPs = new List<ParticleSystem>(elevationFxRoot.GetComponentsInChildren<ParticleSystem>(true));

        foreach (var ps in _elevationPs)
        {
            if (!ps) continue;
            var em = ps.emission;
            em.enabled = true;

            // Initiale Rate auf "From" setzen (konstant)
            var curve = em.rateOverTime;
            curve.mode = ParticleSystemCurveMode.Constant;
            curve.constant = elevationFxRateFrom;
            em.rateOverTime = curve;

            if (!ps.isPlaying) ps.Play(true);
        }

        _elevFxActive = true;
    }

    private void UpdateElevationFX(float progress01)
    {
        if (!_elevFxActive || _elevationPs == null) return;
        float r = Mathf.Lerp(elevationFxRateFrom, elevationFxRateTo, Mathf.Clamp01(progress01));

        foreach (var ps in _elevationPs)
        {
            if (!ps) continue;
            var em = ps.emission;
            var curve = em.rateOverTime;
            curve.mode = ParticleSystemCurveMode.Constant;
            curve.constant = r;
            em.rateOverTime = curve;
        }
    }

    private void StopElevationFX(bool clear = false)
    {
        if (_elevationPs == null) return;

        foreach (var ps in _elevationPs)
        {
            if (!ps) continue;

            // Emission auf 0 und sauber stoppen
            var em = ps.emission;
            var curve = em.rateOverTime;
            curve.mode = ParticleSystemCurveMode.Constant;
            curve.constant = 0f;
            em.rateOverTime = curve;

            ps.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : elevationFxStopBehavior);
        }

        _elevFxActive = false;
    }

    private void StartElevationAudio()
    {
        if (elevationAudio == null)
            throw new MissingReferenceException("[FlyingDroneBehaviour] 'elevationAudio' ist nicht gesetzt.");

        // Abbruch von evtl. laufendem Fade-Out
        if (_elevAudioFadeCo != null) { StopCoroutine(_elevAudioFadeCo); _elevAudioFadeCo = null; }

        elevationAudio.loop = true;
        elevationAudio.volume = elevationAudioVolFrom;
        elevationAudio.pitch = elevationAudioPitchFrom;

        if (!elevationAudio.isPlaying) elevationAudio.Play();
    }

    private void UpdateElevationAudio(float progress01)
    {
        if (elevationAudio == null)
            throw new MissingReferenceException("[FlyingDroneBehaviour] 'elevationAudio' ist nicht gesetzt.");

        float k = Mathf.Clamp01(progress01);
        elevationAudio.volume = Mathf.Lerp(elevationAudioVolFrom, elevationAudioVolTo, k);
        elevationAudio.pitch = Mathf.Lerp(elevationAudioPitchFrom, elevationAudioPitchTo, k);
    }

    private void StopElevationAudio(bool immediate = false)
    {
        if (elevationAudio == null) return;
        if (!elevationAudio.isPlaying) return;

        if (_elevAudioFadeCo != null) { StopCoroutine(_elevAudioFadeCo); _elevAudioFadeCo = null; }

        if (immediate || elevationAudioRelease <= 0f)
        {
            elevationAudio.volume = 0f;
            elevationAudio.Stop();
            elevationAudio.volume = elevationAudioVolFrom;
            elevationAudio.pitch = elevationAudioPitchFrom;
            return;
        }

        _elevAudioFadeCo = StartCoroutine(Co_FadeOutElevationAudio(elevationAudio, elevationAudioRelease));
    }

    private System.Collections.IEnumerator Co_FadeOutElevationAudio(AudioSource src, float seconds)
    {
        float startVol = src.volume;
        float t = 0f;
        while (t < seconds && src != null)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / seconds);
            src.volume = startVol * k;
            yield return null;
        }
        if (src != null)
        {
            src.Stop();
            src.volume = elevationAudioVolFrom;  // Reset
            src.pitch = elevationAudioPitchFrom;
        }
        _elevAudioFadeCo = null;
    }

    #endregion
    #endregion

    #region LOSHelpers
    private void DebugStoreLoSCapsule(Vector3 a, Vector3 b, float r, bool ok)
    {
        _dbgCapA = a; _dbgCapB = b; _dbgCapR = r; _dbgCapOK = ok;
        _dbgCapUntil = Application.isPlaying ? Time.time + Mathf.Max(0.02f, debugCapsulePersist) : 0f;
    }
    #endregion

    #region SetPlayerAsDirectTargetWithOffset // momentarily unncecessary

    /// <summary>
    /// Macht den gegebenen Transform (z. B. Player) zum Ziel, aber mit Y-Offset.
    /// Aktualisiert die Zielposition in dem angegebenen Zeitintervall.
    /// Bricht Waypoint-Modus ab, bis StopFollowingDynamicTarget() aufgerufen wird.
    /// </summary>
    public void SetPlayerAsTargetWithYOffset(Transform playerTransform, float updateIntervalSeconds, float yOffset = 10f, bool useUnscaledTime = false)
    {
        // PatrolPoints/klassisches Target ausblenden, solange wir dynamisch folgen
        usePatrolPoints = false;
        target = null;

        _useDynamicTarget = true;

        if (_dynamicTargetRoutine != null)
            StopCoroutine(_dynamicTargetRoutine);

        _dynamicTargetRoutine = StartCoroutine(DynamicTargetFollowRoutine(playerTransform, updateIntervalSeconds, yOffset, useUnscaledTime));
    }

    private System.Collections.IEnumerator DynamicTargetFollowRoutine(Transform follow, float interval, float yOffset, bool unscaled)
    {
        // Mindestintervall vermeiden
        float dt = Mathf.Max(0.01f, interval);

        while (_useDynamicTarget)
        {
            if (follow == null)
                break;

            _dynamicTargetPos = follow.position + new Vector3(0f, yOffset, 0f);

            if (unscaled)
                yield return new WaitForSecondsRealtime(dt);
            else
                yield return new WaitForSeconds(dt);
        }

        _useDynamicTarget = false;
        _dynamicTargetRoutine = null;
    }

    /// <summary>
    /// Beendet das dynamische Folgen eines Transforms als Ziel.
    /// Danach greifen wieder PatrolPoints oder 'target' (je nach Einstellung).
    /// </summary>
    public void StopFollowingDynamicTarget()
    {
        _useDynamicTarget = false;
        if (_dynamicTargetRoutine != null)
        {
            StopCoroutine(_dynamicTargetRoutine);
            _dynamicTargetRoutine = null;
        }
    }

    #endregion

    #region EyeControls

    #region SearchLightSweep

    /// <summary>
    /// Startet die Suchbewegung des Scheinwerfers in einem Kegel um die Vorwärtsrichtung der Drohne.
    /// Einmal aufrufen reicht. Die Rotation läuft, bis StopSearchlightSweep() gerufen wird.
    /// coneHalfAngleDeg: 0..180 (Halbwinkel des Kegels)
    /// minSeparationDeg: Mindestwinkel zwischen zwei aufeinanderfolgenden Zielrichtungen
    /// angularSpeedDegPerSec: Rotationsgeschwindigkeit in Grad/Sekunde
    /// dwellTimeRange: zufällige Verweilzeit [min, max] am Ziel, bevor der nächste Target-Winkel gewählt wird
    /// </summary>
    /// 
    public void StartSearchlightSweep(
        float coneHalfAngleDeg = 30f,
        float minSeparationDeg = 15f,
        float angularSpeedDegPerSec = 30f,
        Vector2 dwellTimeRange = default
    )
    {
        if (searchLight == null)
        {
            // Falls nichts gesetzt ist, versuche ein Kind mit Light-Komponente zu finden.
            // Optional, kannst du auch entfernen und manuell im Inspector setzen.
            var light = GetComponentInChildren<Light>();
            if (light != null) searchLight = light.transform;
        }

        if (searchLight == null) return; // nichts zu tun

        if (dwellTimeRange == default) dwellTimeRange = new Vector2(0.6f, 1.2f);
        if (dwellTimeRange.x > dwellTimeRange.y)
            dwellTimeRange = new Vector2(dwellTimeRange.y, dwellTimeRange.x);

        coneHalfAngleDeg = Mathf.Clamp(coneHalfAngleDeg, 0f, 180f);
        minSeparationDeg = Mathf.Clamp(minSeparationDeg, 0f, Mathf.Max(0f, 2f * coneHalfAngleDeg));

        if (_searchlightRoutine != null) StopCoroutine(_searchlightRoutine);

        // Initialwert: Kegel zeigt standardmäßig nach unten (Welt-Down)
        // -> sorgt dafür, dass die erste zufällige Richtung auch wirklich im „Down-Kegel“ liegt
        _lastSearchDirWorld = Vector3.down;

        _searchlightRoutine = StartCoroutine(
            SearchlightSweepRoutine(coneHalfAngleDeg, minSeparationDeg, angularSpeedDegPerSec, dwellTimeRange)
        );
        scanner.SetActive(true);                                // optional: Audio/FX/Speed-Anpassungen
        CheckScannerToggleOnce();
    }

    /// <summary>
    /// Stoppt die Suchbewegung des Scheinwerfers.
    /// </summary>
    public void StopSearchlightSweep()
    {
        if (_searchlightRoutine != null)
        {
            StopCoroutine(_searchlightRoutine);
            _searchlightRoutine = null;
        }
    }

    /// <summary>
    /// Coroutine: wählt fortlaufend zufällige Zielrichtungen im Kegel und
    /// rotiert den Scheinwerfer mit Slerp/RotateTowards dorthin.
    /// </summary>
    private System.Collections.IEnumerator SearchlightSweepRoutine(
        float coneHalfAngleDeg,
        float minSeparationDeg,
        float angularSpeedDegPerSec,
        Vector2 dwellTimeRange
    )
    {
        // Sicherheitsnetze
        angularSpeedDegPerSec = Mathf.Max(1f, angularSpeedDegPerSec);
        float minDwell = Mathf.Max(0f, dwellTimeRange.x);
        float maxDwell = Mathf.Max(minDwell, dwellTimeRange.y);

        while (true)
        {
            // Basis-Ausrichtung ist die Abwärtsrichtung der Drohne
            Vector3 baseForward = Vector3.down;
            if (baseForward.sqrMagnitude < 1e-6f) baseForward = Vector3.down;

            // Nächste Zielrichtung im Kegel bestimmen (mit Mindestabstand)
            Vector3 nextDir = PickDirectionWithSeparation(baseForward, coneHalfAngleDeg, _lastSearchDirWorld, minSeparationDeg);

            // Zielrotation
            Quaternion targetRotWorld = Quaternion.LookRotation(nextDir, Vector3.up);

            // pro Frame rotieren (in Weltkoordinaten, parent-unabhängig)
            while (true)
            {
                if (searchLight == null) yield break;
                float angle = Quaternion.Angle(searchLight.rotation, targetRotWorld);
                if (angle < 0.25f) break;

                float step = angularSpeedDegPerSec * Time.deltaTime;
                RotateChildWorldTowards(searchLight, targetRotWorld, step);
                yield return null;
            }

            _lastSearchDirWorld = nextDir; // also an dieser stelle? 

            // Kurze Verweilzeit am Ziel
            float dwell = (maxDwell > minDwell) ? Random.Range(minDwell, maxDwell) : minDwell;
            if (dwell > 0f) yield return new WaitForSeconds(dwell);
        }
    }

    /// <summary>
    /// Liefert eine zufällige Welt-Richtung im Kegel um baseForward (Halbwinkel in Grad),
    /// die mindestens 'minSeparationDeg' von 'lastDirWorld' entfernt ist.
    /// </summary>
    private Vector3 PickDirectionWithSeparation(Vector3 baseForward, float coneHalfAngleDeg, Vector3 lastDirWorld, float minSeparationDeg, int maxTries = 12)
    {
        baseForward.Normalize();
        if (lastDirWorld.sqrMagnitude < 1e-6f) lastDirWorld = baseForward;

        float bestAngle = -1f;
        Vector3 bestDir = baseForward;

        for (int i = 0; i < maxTries; i++)
        {
            Vector3 cand = RandomDirectionInCone(baseForward, coneHalfAngleDeg);
            float a = Vector3.Angle(cand, lastDirWorld);

            if (a >= minSeparationDeg) return cand; // gut genug, sofort nehmen
            if (a > bestAngle) { bestAngle = a; bestDir = cand; }
        }

        // Fallback: beste gefundene Richtung (größter Abstand)
        return bestDir;
    }

    /// <summary>
    /// Gleichmäßig verteilte Zufallsrichtung auf einer Kugelkappe (Kegel) um 'forward'.
    /// coneHalfAngleDeg ist der Halbwinkel des Kegels.
    /// </summary>
    private Vector3 RandomDirectionInCone(Vector3 forward, float coneHalfAngleDeg)
    {
        forward.Normalize();
        float coneRad = Mathf.Deg2Rad * Mathf.Clamp(coneHalfAngleDeg, 0f, 180f);

        // Gleichverteilung auf Kugelkappe:
        // cos(theta) in [cos(cone), 1], phi in [0, 2pi)
        float u = Random.value;
        float v = Random.value;
        float cosCone = Mathf.Cos(coneRad);
        float cosTheta = 1f - v * (1f - cosCone);
        float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * Mathf.PI * u;

        // Lokale Richtung um +Z
        Vector3 local = new Vector3(Mathf.Cos(phi) * sinTheta, Mathf.Sin(phi) * sinTheta, cosTheta);

        // Auf 'forward' ausrichten
        Quaternion toForward = Quaternion.FromToRotation(Vector3.forward, forward);
        return toForward * local;
    }

    #endregion

    #region AimEyeLight
    /// <summary>
    /// Richtet den Scheinwerfer auf ein Ziel-Transform aus.
    /// angularSpeedDegPerSec: Rotationsgeschwindigkeit in Grad/Sek.
    /// followContinuously: true = dauerhaft folgen; false = einmalig ausrichten und stoppen.
    /// worldOffset: optionaler Offset (Weltkoords), z. B. (0, 1.5, 0) um etwas über den Pivot zu zielen.
    /// useUnscaledTime: true = unabhängig von Time.timeScale.
    /// stopAngleDeg: Schwellwert, unter dem bei 'followContinuously = false' beendet wird.
    /// </summary>
    public void AimSearchlightAt(
        Transform target,
        float angularSpeedDegPerSec = 180f,
        bool followContinuously = true,
        Vector3 worldOffset = default,
        bool useUnscaledTime = false,
        float stopAngleDeg = 0.25f)
    {
        if (searchLight == null)
        {
            var light = GetComponentInChildren<Light>();
            if (light != null) searchLight = light.transform;
            if (searchLight == null) return;
        }

        // Laufende Sweep- oder Aim-Routinen stoppen, damit nur eine Kontrolle aktiv ist
        if (_searchlightRoutine != null) { StopCoroutine(_searchlightRoutine); _searchlightRoutine = null; }
        if (_searchlightAimRoutine != null) { StopCoroutine(_searchlightAimRoutine); _searchlightAimRoutine = null; }

        _searchlightAimRoutine = StartCoroutine(SearchlightAimRoutine(
            target, angularSpeedDegPerSec, followContinuously, worldOffset, useUnscaledTime, stopAngleDeg));
    }

    /// <summary>
    /// Stoppt das Ausrichten auf ein Ziel. Danach macht der Scheinwerfer nichts weiter,
    /// bis du wieder AimSearchlightAt(...) oder StartSearchlightSweep(...) aufrufst.
    /// </summary>
    public void StopAimingSearchlight()
    {
        if (_searchlightAimRoutine != null)
        {
            StopCoroutine(_searchlightAimRoutine);
            _searchlightAimRoutine = null;
        }
    }

    private System.Collections.IEnumerator SearchlightAimRoutine(
        Transform target,
        float angularSpeedDegPerSec,
        bool followContinuously,
        Vector3 worldOffset,
        bool unscaled,
        float stopAngleDeg)
    {
        angularSpeedDegPerSec = Mathf.Max(1f, angularSpeedDegPerSec);
        stopAngleDeg = Mathf.Max(0.01f, stopAngleDeg);

        while (true)
        {
            if (searchLight == null || target == null) yield break;

            Vector3 aimPoint = target.position + worldOffset;
            Vector3 dir = aimPoint - searchLight.position;
            if (dir.sqrMagnitude < 1e-6f)
            {
                if (!followContinuously) break;
                yield return null;
                continue;
            }

            Quaternion targetRotWorld = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            float step = angularSpeedDegPerSec * dt;

            // statt: searchLight.rotation = Quaternion.RotateTowards(...)
            SetChildWorldRotation(searchLight, targetRotWorld);

            float angle = Quaternion.Angle(searchLight.rotation, targetRotWorld);
            if (!followContinuously && angle <= stopAngleDeg) break;

            yield return null;

        }

        _searchlightAimRoutine = null;
    }

    //// Dauerhaft auf ein Transform ausrichten (folgt Bewegungen), 150°/s:
    //AimSearchlightAt(playerHeadTransform, 150f, true);
    //
    //// Einmalig ausrichten und stoppen, 200°/s, Ziel etwas höher:
    //AimSearchlightAt(targetTransform, 200f, false, new Vector3(0f, 1.5f, 0f));
    //
    //// Beenden:
    //StopAimingSearchlight();
    //
    //// Optional danach wieder Suche starten:
    //StartSearchlightSweep(70f, 30f, 120f, new Vector2(0.2f, 0.5f));

    // Dreht ein Child in Weltkoordinaten – unabhängig von der Parent-Rotation.
    private void RotateChildWorldTowards(Transform child, Quaternion targetWorldRot, float maxDegThisFrame)
    {
        if (child == null) return;

        Quaternion currentWorld = child.rotation;
        Quaternion nextWorld = Quaternion.RotateTowards(currentWorld, targetWorldRot, maxDegThisFrame);

        if (child.parent != null)
            child.localRotation = Quaternion.Inverse(child.parent.rotation) * nextWorld;
        else
            child.rotation = nextWorld;
    }

    private void SetChildWorldRotation(Transform child, Quaternion targetWorldRot)
    {
        if (child == null) return;

        if (child.parent != null)
            child.localRotation = Quaternion.Inverse(child.parent.rotation) * targetWorldRot;
        else
            child.rotation = targetWorldRot;
    }

    #endregion

    #region ElevatedPatrolSearchLightManipulation
    private void TriggerSearchlightEnableAfterDelay(float delaySec)
    {
        if (_searchlightReenableCo != null) StopCoroutine(_searchlightReenableCo);
        _searchlightReenableCo = StartCoroutine(Co_EnableSearchlightAfterDelay(delaySec));
    }

    private IEnumerator Co_EnableSearchlightAfterDelay(float delaySec)
    {
        yield return new WaitForSeconds(delaySec);

        // nur reaktivieren, wenn wir inzwischen im Patrol-State sind
        if (agentState == AgentState.Patrol)
            StartSearchlightSweep();
        scanner.SetActive(true);
        CheckScannerToggleOnce();
        _searchlightReenableCo = null;
    }

    private void CancelPendingSearchlightEnable()
    {
        if (_searchlightReenableCo != null)
        {
            StopCoroutine(_searchlightReenableCo);
            _searchlightReenableCo = null;
        }
    }
    #endregion
    #endregion

    #region HemisphereCheckMethods


    // Kurzform: Center == Target
    public void StartHemisphereScannerRange(
        Transform centerAndTarget,
        float minRadius,
        float maxRadius,
        string tagToCheck = null,
        int points = -1,
        float tickInterval = -1f)
    {
        hemisphereCenter = centerAndTarget != null ? centerAndTarget : transform;
        scanTarget = centerAndTarget;

        // Tag explizit setzen (Default = Player)
        sightTag = string.IsNullOrEmpty(tagToCheck) ? TAG_PLAYER : tagToCheck;

        hemisphereMinRadius = Mathf.Max(0.01f, minRadius);
        hemisphereMaxRadius = Mathf.Max(hemisphereMinRadius, maxRadius);

        StartHemisphereScannerInternal(points, tickInterval);
    }

    // Langform: Center und Target getrennt
    public void StartHemisphereScannerRange(
        Transform center,
        Transform target,
        float minRadius,
        float maxRadius,
        string tagToCheck = null,
        int points = -1,
        float tickInterval = -1f)
    {
        hemisphereCenter = center != null ? center : transform;
        scanTarget = target != null ? target : null;

        // Tag explizit setzen (Default = Player)
        sightTag = string.IsNullOrEmpty(tagToCheck) ? TAG_PLAYER : tagToCheck;

        hemisphereMinRadius = Mathf.Max(0.01f, minRadius);
        hemisphereMaxRadius = Mathf.Max(hemisphereMinRadius, maxRadius);

        StartHemisphereScannerInternal(points, tickInterval);
    }

    // ===== Optional: Kontext zur Laufzeit wechseln =====
    public void SetScanContext(Transform center, Transform target, string tagToCheck = null, bool restart = true, bool clearTrails = true)
    {
        if (center != null) hemisphereCenter = center;
        if (target != null) scanTarget = target;

        // Tag explizit setzen (Default = Player)
        sightTag = string.IsNullOrEmpty(tagToCheck) ? TAG_PLAYER : tagToCheck;

        if (restart && _scanActive)
        {
            EndWave(clearTrails);
            _nextReseedTime = 0f;
        }
    }

    // Liefert die zu testenden Radien in definierter Reihenfolge
    private System.Collections.Generic.IEnumerable<float> EnumerateRadii()
    {
        int steps = Mathf.Max(1, hemisphereRadialSteps);
        float minR = Mathf.Max(0.01f, hemisphereMinRadius);
        float maxR = Mathf.Max(minR, hemisphereMaxRadius);

        for (int s = 0; s < steps; s++)
        {
            float t = (steps == 1) ? 1f : s / (float)(steps - 1); // 0..1
            if (radialScanFarToNear) t = 1f - t;                 // optional weit->nah
            yield return Mathf.Lerp(minR, maxR, t);
        }
    }


    // Gleichmäßig verteilte Richtungen (Fibonacci-Sphere), grob auf refDir ausgerichtet
    private System.Collections.Generic.IEnumerable<Vector3> FibonacciSphere(int n, Vector3 refDir)
    {
        n = Mathf.Max(6, n);
        float offset = 2f / n;
        float increment = Mathf.PI * (3f - Mathf.Sqrt(5f)); // goldener Winkel

        // Basis: um +Z (Vector3.forward) erzeugen, dann auf refDir drehen
        Quaternion toRef = Quaternion.FromToRotation(Vector3.forward, (refDir.sqrMagnitude > 1e-6f ? refDir.normalized : Vector3.forward));

        for (int i = 0; i < n; i++)
        {
            float y = i * offset - 1f + (offset / 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = i * increment;
            float x = Mathf.Cos(phi) * r;
            float z = Mathf.Sin(phi) * r;

            Vector3 dirLocal = new Vector3(x, y, z); // um +Z
            yield return toRef * dirLocal;           // auf refDir ausgerichtet
        }
    }

    // Optionaler, wiederverwendbarer Buffer (oben in der Klasse anlegen)
    private readonly Collider[] _checkBuf = new Collider[32];

    /// <summary>
    /// Prüft, ob eine Position den momentanen Avoid-Radius nicht verletzt.
    /// true = frei (kein Collider innerhalb avoidRadius [+ extraMargin]).
    /// </summary>
    /// <param name="position">Weltposition, die geprüft werden soll</param>
    /// <param name="extraMargin">Zusätzlicher Puffer zum avoidRadius (>= 0)</param>
    /// <param name="includeGround">Ob zusätzlich der groundMask berücksichtigt werden soll</param>
    public bool PositionRespectsAvoidRadius(Vector3 position, float extraMargin = 0f, bool includeGround = false)
    {
        float r = Mathf.Max(0f, avoidRadius + extraMargin);

        // Maske zusammenstellen
        LayerMask mask = obstacleMask;
        if (includeGround && groundMask != 0) mask |= groundMask;

        // NonAlloc-Variante, um GC zu vermeiden
        int count = Physics.OverlapSphereNonAlloc(
            position, r, _checkBuf, mask, QueryTriggerInteraction.Collide);

        if (count == 0) return true;

        // Eigene Collider ignorieren
        for (int i = 0; i < count; i++)
        {
            Collider c = _checkBuf[i];
            if (c == null) continue;
            if (c == _selfCollider) continue; // sich selbst ignorieren

            // Irgendein anderer Collider liegt im Radius -> verletzt
            return false;
        }

        // Es waren nur eigene/ungültige Einträge
        return true;
    }

    /// <summary>
    /// Startet den kontinuierlichen Hemisphären-Scan.
    /// - Rotation ist fix (Welt: +Y nach oben). Nur die Position des Centers folgt.
    /// - Punkte sind gleichmäßig verteilt; der Top-Punkt (Vector3.up) ist garantiert enthalten.
    /// </summary>
    // Privat: baut Arrays auf und startet den Scanner. Nutzt bereits gesetzte Min/Max.
    private void StartHemisphereScannerInternal(int points = -1, float tickInterval = -1f)
    {
        _hemiHasAdoptedOnce = false;
        _hemiAdoptionPending = false;

        if (points > 0) hemispherePoints = Mathf.Max(5, points);
        if (tickInterval > 0f) scanTickInterval = tickInterval;

        if (hemisphereCenter == null) hemisphereCenter = (player != null ? player : transform);

        _hemiLocalDirs = GenerateHemisphereDirections(hemispherePoints, true);
        _hemiWorldPoints = new Vector3[_hemiLocalDirs.Length];
        _state = new SampleState[_hemiLocalDirs.Length];
        _lastTestTime = new float[_hemiLocalDirs.Length];
        _order = new int[_hemiLocalDirs.Length];
        _testedEpoch = new int[_hemiLocalDirs.Length];
        for (int i = 0; i < _testedEpoch.Length; i++) _testedEpoch[i] = 0;

        _waveActive = false;
        _epoch = 0;
        _waveCursor = 0;

        for (int i = 0; i < _state.Length; i++)
        {
            _state[i] = SampleState.Untested;
            _lastTestTime[i] = -999f;
            _order[i] = i;
        }
        _cursor = 0;
        _seedIndex = -1;
        _trails.Clear();

        _scanActive = true;
        if (_scannerCo != null) StopCoroutine(_scannerCo);
        _scannerCo = StartCoroutine(ScannerRoutine());
    }


    /// <summary>Stoppt den Hemisphären-Scan und löscht die Visualisierung.</summary>
    public void StopHemisphereScanner(bool clearTrails = false)
    {
        _scanActive = false;
        if (clearTrails) _trails.Clear();
        if (_scannerCo != null) { StopCoroutine(_scannerCo); _scannerCo = null; }
    }


    private System.Collections.IEnumerator ScannerRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.01f, scanTickInterval));

        while (_scanActive)
        {
            if (hemisphereCenter == null) { yield return wait; continue; }

            // 1) Weltpunkte updaten (keine Rotation; Welt-Up fix)
            Vector3 center = hemisphereCenter.position;
            float midR = HemisphereMidRadius;
            for (int i = 0; i < _hemiLocalDirs.Length; i++)
                _hemiWorldPoints[i] = center + _hemiLocalDirs[i] * midR;

            // 2) Direkt-Sicht vom Searchlight auf das Ziel prüfen (Gate für die Welle)
            Transform aimT = (scanTarget != null) ? scanTarget : player;

            bool directSight = false;
            if (searchLight != null && aimT != null)
            {
                if (useThickLoS)
                    directSight = HasLineOfSightCapsule(searchLight.position, aimT.position, losCapsuleRadius, sightTag);
                else
                    directSight = HasLineOfSight(searchLight.position, aimT.position, sightTag);
            }

            // 2a) Wenn Direkt-Sicht besteht → laufende Welle sofort stoppen, nichts neu zuweisen
            if (directSight)
            {
                if (_waveActive) EndWave(clearTrails: false);

                // Wenn die aktuelle Position zulässig ist, sofort „adoptieren“
                if (!_hemiHasAdoptedOnce && PositionRespectsAvoidRadius(transform.position, validStayExtraMargin, false))
                {
                    _hemiCurrentGoal = transform.position;
                    _hemiAdoptionPending = true;  // MoveAndAvoid triggert Adoption im selben/ nächsten Physik-Frame
                }

                yield return wait;
                continue;
            }


            // 3) Wenn keine Direkt-Sicht: ggf. Welle starten
            if (!_waveActive)
                BeginWave(center, aimT);

            // 4) Falls Welle aktiv: ressourcenschonend fortsetzen
            if (_waveActive)
                ContinueWave(aimT);

            _sinceLastSelection += scanTickInterval;

            yield return wait;
        }
    }


    private void ComputeSeedAndOrder()
    {
        Vector3 a = (searchLight != null) ? searchLight.position : transform.position;
        Vector3 b = (scanTarget != null) ? scanTarget.position : (player != null ? player.position : a + Vector3.forward);

        // 0) Prüfen, ob sich die Linie „relevant“ geändert hat
        bool shouldReseed = false;
        if (Time.time >= _nextReseedTime)
        {
            // Winkeländerung der Linienrichtung
            Vector3 dPrev = (_lastLineB - _lastLineA);
            Vector3 dNow = (b - a);
            float ang = 0f;
            if (dPrev.sqrMagnitude > 1e-6f && dNow.sqrMagnitude > 1e-6f)
                ang = Vector3.Angle(dPrev, dNow);

            // auch Positionssprung der Endpunkte erlauben
            bool endpointsMoved = ((_lastLineA - a).sqrMagnitude > 0.25f || (_lastLineB - b).sqrMagnitude > 0.25f);

            if (ang >= reseedAngleThresholdDeg || endpointsMoved)
            {
                shouldReseed = true;
                _nextReseedTime = Time.time + reseedMinInterval;
            }
        }

        // 1) Seed neu bestimmen
        int newSeed = FindPointClosestToLine(a, b, _hemiWorldPoints);

        // 2) Reihenfolge ab neuem Seed aufbauen
        BuildWaveOrder(newSeed, hemisphereCenter.position, _hemiWorldPoints, ringStepDeg, _order);

        // 3) Cursor & Seed setzen
        _seedIndex = newSeed;
        _cursor = 0;

        // 4) Wenn „relevant“ geändert: innerste Ringe resetten (Status + Cooldown),
        //    damit die Welle wieder *wirklich* am Seed startet
        if (shouldReseed)
            ResetInnerRingsAroundSeed(_seedIndex, hemisphereCenter.position, _hemiWorldPoints, ringStepDeg, reseedInnerRingsToReset);

        // 5) Seed optisch als queued markieren
        if (_state[_seedIndex] == SampleState.Untested) _state[_seedIndex] = SampleState.Queued;

        // 6) Linie merken
        _lastLineA = a; _lastLineB = b;
    }

    private int NextIndexToTest()
    {
        int n = _order.Length;
        float now = Time.time;
        for (int k = 0; k < n; k++)
        {
            int i = _order[(_cursor + k) % n];
            if (now - _lastTestTime[i] < rescanCooldown) continue; // kürzlich geprüft
            if (_state[i] == SampleState.Selected) continue;       // bereits gutes Ziel
            _cursor = (_cursor + k + 1) % n;
            _state[i] = SampleState.Queued;
            return i;
        }
        return -1; // nichts zu tun
    }

    // ---------- Status & Trails ----------
    private void MarkBlocked(int idx)
    {
        _state[idx] = SampleState.TestedBlocked;
        _lastTestTime[idx] = Time.time;
    }

    private void LogTrail(Vector3 from, Vector3 to, bool ok)
    {
        if (!drawRays) return;
        _trails.Add(new RayTrail { from = from, to = to, t0 = Time.time, ok = ok });
        if (_trails.Count > maxRayTrails) _trails.RemoveAt(0);
    }

    // ---------- Mathe/Helpers ----------
    private int FindPointClosestToLine(Vector3 a, Vector3 b, Vector3[] pts)
    {
        Vector3 ab = b - a;
        float abSqr = Mathf.Max(1e-6f, ab.sqrMagnitude);
        int best = 0; float bestD2 = float.PositiveInfinity;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 ap = pts[i] - a;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / abSqr);
            Vector3 c = a + ab * t;
            float d2 = (pts[i] - c).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = i; }
        }
        return best;
    }

    private void BuildWaveOrder(int seedIdx, Vector3 center, Vector3[] worldPts, float ringStepDeg,
                                int[] outOrder)
    {
        int n = worldPts.Length;
        var tmp = new System.Collections.Generic.List<(int idx, float polar, float az)>(n);

        Vector3 seedDir = (worldPts[seedIdx] - center).normalized;

        Vector3 w1 = Vector3.Cross(seedDir, Vector3.right);
        if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.Cross(seedDir, Vector3.forward);
        w1.Normalize();
        Vector3 w2 = Vector3.Cross(seedDir, w1).normalized;

        for (int i = 0; i < n; i++)
        {
            Vector3 d = (worldPts[i] - center).normalized;
            float polar = Mathf.Acos(Mathf.Clamp(Vector3.Dot(seedDir, d), -1f, 1f));
            float x = Vector3.Dot(d, w1);
            float y = Vector3.Dot(d, w2);
            float az = Mathf.Atan2(y, x);
            tmp.Add((i, polar, az));
        }

        float ringStep = Mathf.Deg2Rad * Mathf.Clamp(ringStepDeg, 2f, 30f);

        // Buckets nach Ring
        var buckets = new System.Collections.Generic.SortedDictionary<int, System.Collections.Generic.List<(int, float)>>();
        for (int i = 0; i < tmp.Count; i++)
        {
            int ring = Mathf.RoundToInt(tmp[i].polar / ringStep);
            if (!buckets.TryGetValue(ring, out var list))
            {
                list = new System.Collections.Generic.List<(int, float)>();
                buckets[ring] = list;
            }
            list.Add((tmp[i].idx, tmp[i].az));
        }

        // Zusammensetzen: ringweise, innerhalb Ring nach Azimut
        int ptr = 0;
        foreach (var kv in buckets)
        {
            kv.Value.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            foreach (var pair in kv.Value) outOrder[ptr++] = pair.Item1;
        }

        // Seed an Anfang rotieren
        int pos = System.Array.IndexOf(outOrder, seedIdx);
        if (pos > 0)
        {
            // zyklische Rotation
            var buf = new int[pos];
            System.Array.Copy(outOrder, 0, buf, 0, pos);
            System.Array.Copy(outOrder, pos, outOrder, 0, outOrder.Length - pos);
            System.Array.Copy(buf, 0, outOrder, outOrder.Length - pos, pos);
        }
    }

    // Reuse: Aufenthaltsprüfung & LoS (du hast PositionRespectsAvoidRadius(...) schon oben)
    private bool HasLineOfSight(Vector3 origin, Vector3 aimPoint, string requiredTag)
    {
        Vector3 dir = aimPoint - origin;
        float dist = dir.magnitude;
        if (dist < 1e-4f) return false;
        dir /= dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, hemiSphereRaycastMask, QueryTriggerInteraction.Ignore))
            return hit.collider != null && hit.collider.CompareTag(requiredTag);
        return true; // nichts getroffen -> frei
    }

    // ---------- Gizmos ----------
    // Komplett ersetzen:
    private int _gizmoSkip = 0;
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Mittelpunkt bestimmen
        Vector3 center = (hemisphereCenter != null) ? hemisphereCenter.position : transform.position;

        // Editor-Preview / Sicherstellen, dass Weltpunkte existieren
        if (_hemiLocalDirs != null)
        {
            int n = _hemiLocalDirs.Length;
            if (_hemiWorldPoints == null || _hemiWorldPoints.Length != n)
                _hemiWorldPoints = new Vector3[n];

            float midR = HemisphereMidRadius;
            for (int i = 0; i < n; i++)
                _hemiWorldPoints[i] = center + _hemiLocalDirs[i] * midR;
        }
        else if (_hemiWorldPoints == null)
        {
            return; // nichts zu zeichnen
        }

        // Reduzierte Zeichenfrequenz
        if (++_gizmoSkip % Mathf.Max(1, gizmoEveryNth) != 0) return;

        float pointRadius = Mathf.Max(0.02f, HemisphereMidRadius * gizmoPointSize);

        // --- Ringe (Min/Max/Mid) ---
#if UNITY_EDITOR
        // Min/Max sichtbar machen
        UnityEditor.Handles.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        if (hemisphereMinRadius > 0.001f)
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, hemisphereMinRadius);
        if (hemisphereMaxRadius > hemisphereMinRadius + 0.001f)
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, hemisphereMaxRadius);

        // Aktueller Mid-Radius als Referenz
        UnityEditor.Handles.color = new Color(0.1f, 0.9f, 0.9f, 0.35f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, HemisphereMidRadius);
#endif

        // --- Punkte zeichnen ---
        for (int i = 0; i < _hemiWorldPoints.Length; i++)
        {
            Color col = Color.gray;
            if (_state != null && i < _state.Length)
            {
                switch (_state[i])
                {
                    case SampleState.Untested: col = Color.gray; break;
                    case SampleState.Queued: col = new Color(0.3f, 0.6f, 1f, 1f); break; // blau
                    case SampleState.TestedBlocked: col = Color.red; break;
                    case SampleState.TestedOK: col = Color.green; break;
                    case SampleState.Selected: col = Color.white; break;
                }
            }
            if (i == _seedIndex) col = Color.cyan; // Seed hervorheben

            Gizmos.color = col;
            Gizmos.DrawSphere(_hemiWorldPoints[i], pointRadius);
        }

        // --- Seed-Verbindung (optisch) ---
#if UNITY_EDITOR
        if (_seedIndex >= 0 && _seedIndex < _hemiWorldPoints.Length)
        {
            UnityEditor.Handles.color = new Color(0.3f, 1f, 1f, 0.8f);
            UnityEditor.Handles.DrawLine(center, _hemiWorldPoints[_seedIndex]);
        }
#endif

        // --- Ray-Trails mit Ausblenden ---
        if (drawRays && _trails != null)
        {
            float now = Application.isPlaying ? Time.time : 0f;

            for (int i = 0; i < _trails.Count; i++)
            {
                float a = 1f;
                if (Application.isPlaying && rayFadeSeconds > 0f)
                    a = Mathf.Clamp01(1f - (now - _trails[i].t0) / rayFadeSeconds);

                if (a <= 0f) continue;

                Gizmos.color = _trails[i].ok
                    ? new Color(0f, 1f, 0.7f, a)     // Treffer ok
                    : new Color(1f, 0.4f, 0f, a);    // blockiert
                Gizmos.DrawLine(_trails[i].from, _trails[i].to);
            }

            // Alte Trails ausmisten (ohne GC-Spikes)
            if (Application.isPlaying && rayFadeSeconds > 0f && _trails.Count > 0)
            {
                int keep = 0;
                float cutoff = Time.time - rayFadeSeconds;
                for (int i = 0; i < _trails.Count; i++)
                    if (_trails[i].t0 >= cutoff) _trails[keep++] = _trails[i];
                if (keep < _trails.Count) _trails.RemoveRange(keep, _trails.Count - keep);
            }
        }

        if (!debugDrawLoSCapsule) return;
        if (Application.isPlaying && Time.time > _dbgCapUntil) return;

        DrawWireCapsuleGizmo(_dbgCapA, _dbgCapB, _dbgCapR, _dbgCapOK ? Color.green : Color.red);

        // --- HUD / Info ---
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            string txt =
                $"HemisphereScan\n" +
                $"Pts:{_hemiWorldPoints.Length}  Tick:{scanTickInterval:F2}s  MaxRays/Tick:{maxRaysPerTick}\n" +
                $"Min:{hemisphereMinRadius:F1}  Max:{hemisphereMaxRadius:F1}  Steps:{hemisphereRadialSteps}  Seed:{_seedIndex}  Cursor:{_cursor}";
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(center + Vector3.up * (HemisphereMidRadius + 0.5f), txt);
        }
#endif
    }


    /// <summary>
    /// Erzeugt gleichmäßig verteilte Einheitsvektoren auf der OBEREN Hemisphäre (y >= 0),
    /// in fester Welt-Ausrichtung (kein Rotieren mit dem Player).
    /// Der Vektor (0,1,0) ist garantiert enthalten, wenn ensureTop = true.
    /// </summary>
    /// Erzeugt gleichmäßig verteilte Einheitsvektoren auf der OBEREN Hemisphäre (y >= 0)
    /// mit konstanter Flächendichte bis direkt an den Pol. Der Top-Punkt (0,1,0) ist
    /// garantiert enthalten und „verbraucht“ exakt die Fläche eines Punkts, sodass die
    /// Umgebung nicht ausdünnt.
    ///
    /// count: Gesamtanzahl inkl. Top-Punkt.
    private Vector3[] GenerateHemisphereDirections(int count, bool ensureTop /*ignoriert, da immer true*/)
    {
        count = Mathf.Max(5, count);

        int total = count;      // Gesamtzahl inkl. Nordpol
        int n = total - 1;      // restliche Punkte ohne Nordpol

        var dirs = new Vector3[total];

        // 1) Nordpol fest
        dirs[0] = Vector3.up;

        if (n <= 0) return dirs;

        // 2) Fläche obere Hemisphäre (R=1): 2π
        //    Pro Punkt: A_pt = 2π / total
        //    Polarkappe für Nordpol: 2π * (1 - z0) = 2π / total  =>  z0 = 1 - 1/total
        float z0 = 1f - 1f / total;

        // 3) Equal-Area: z gleichverteilt in [0, z0], φ als Goldwinkel-Sequenz
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f)); // ≈ 2.399963...

        for (int i = 0; i < n; i++)
        {
            // Midpoint-Stratifizierung vermeidet Banding
            float t = (i + 0.5f) / n;   // (0,1)
            float z = t * z0;           // [0, z0]  (oberer Halbraum ohne Polarkappe)
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

            float phi = (i + 0.5f) * golden;

            float x = r * Mathf.Cos(phi);
            float y = z;                // y ist „Up“
            float w = r * Mathf.Sin(phi);

            dirs[i + 1] = new Vector3(x, y, w); // bereits normiert
        }

        return dirs;
    }

    // setzt die Zustände und Cooldowns für die ersten K Ringe (ab Seed) zurück
    private void ResetInnerRingsAroundSeed(int seedIdx, Vector3 center, Vector3[] worldPts, float ringStepDeg, int innerRings)
    {
        int n = worldPts.Length;
        if (n == 0 || innerRings <= 0) return;

        float ringStep = Mathf.Deg2Rad * Mathf.Clamp(ringStepDeg, 2f, 30f);
        Vector3 seedDir = (worldPts[seedIdx] - center).normalized;

        for (int i = 0; i < n; i++)
        {
            Vector3 d = (worldPts[i] - center).normalized;
            float polar = Mathf.Acos(Mathf.Clamp(Vector3.Dot(seedDir, d), -1f, 1f));
            int ring = Mathf.RoundToInt(polar / ringStep);

            if (ring <= innerRings)
            {
                // diesen Sample wieder zum Startkandidaten machen
                _state[i] = SampleState.Untested;
                _lastTestTime[i] = -999f; // Cooldown löschen
            }
        }
    }

    // Startet eine neue Epoche/Welle ab aktuellem Seed
    private void BeginWave(Vector3 center, Transform aimT)
    {
        if (aimT == null || _hemiWorldPoints == null || _hemiWorldPoints.Length == 0) return;

        _epoch++;                    // neue Epoche = frischer Durchlauf
        _waveCursor = 0;
        _trails.Clear();             // optional: alte Ray-Spuren leeren (für sauberes Debug)

        // Seed bestimmen: Punkt mit minimalem Abstand zur Linie (SearchLight -> Target)
        Vector3 a = (searchLight != null) ? searchLight.position : transform.position;
        Vector3 b = aimT.position;
        _seedIndex = FindPointClosestToLine(a, b, _hemiWorldPoints);

        // Wellen-Reihenfolge (Ringe um Seed, innerhalb Ring nach Azimut)
        BuildWaveOrder(_seedIndex, center, _hemiWorldPoints, ringStepDeg, _order);

        // Sichtbarer Reset: Alle Punkte optisch „untested“; testedEpoch bleibt bestehen (Epoche sorgt für Reset)
        for (int i = 0; i < _state.Length; i++)
            _state[i] = SampleState.Untested;

        // Seed als front markieren
        _state[_seedIndex] = SampleState.Queued;

        _waveActive = true;
    }

    // Führt pro Tick bis zu maxRaysPerTick Tests in der Reihenfolge aus.
    // Bricht SOFORT ab, wenn Direkt-Sicht plötzlich wieder besteht oder ein gültiger Punkt gefunden wurde.
    private void ContinueWave(Transform aimT)
    {
        if (!_waveActive || aimT == null) return;

        int fired = 0;
        int budgetThisTick = maxRaysPerTick;
        if (adaptiveRayBudget)
        {
            float t = (budgetRampSeconds <= 0f) ? 1f : Mathf.Clamp01(_sinceLastSelection / budgetRampSeconds);
            budgetThisTick = Mathf.RoundToInt(Mathf.Lerp(raysPerTickMin, raysPerTickMax, t));
        }
        Vector3 center = hemisphereCenter != null ? hemisphereCenter.position : transform.position;

        while (fired < budgetThisTick && _waveActive)
        {
            // Direkt-Sicht? -> sofort stoppen
            if (searchLight != null && HasLineOfSightCapsule(searchLight.position, aimT.position, losCapsuleRadius, sightTag))
            {
                EndWave(clearTrails: false);
                break;
            }

            int idx = NextIndexToTestThisWave();
            if (idx < 0)
            {
                EndWave(clearTrails: false); // alle in dieser Epoche abgearbeitet
                break;
            }

            // Richtung der Probe
            Vector3 dirLocal = _hemiLocalDirs[idx].normalized;
            bool foundOK = false;

            // Entlang der Richtung mehrere Radien prüfen (Min..Max)
            foreach (float radius in EnumerateRadii())
            {
                Vector3 from = center + dirLocal * radius;
                Vector3 to = aimT.position;
                Vector3 d = to - from;
                float dist = d.magnitude;

                bool ok = false;
                if (dist >= 1e-4f)
                {
                    // LoS: dick oder dünn
                    bool losOK = useThickLoS
                        ? HasLineOfSightCapsule(from, aimT.position, losCapsuleRadius, sightTag)
                        : HasLineOfSight(from, aimT.position, sightTag);

                    // Aufenthaltsprüfung am Punkt
                    bool radiusOK = PositionRespectsAvoidRadius(from, validStayExtraMargin, false);

                    ok = losOK && radiusOK;
                }

                LogTrail(from, to, ok);
                fired++; // jeder Radialtest zählt als "RAY"

                if (ok)
                {
                    _dynamicTargetPos = from;
                    _hemiCurrentGoal = from;
                    _hemiAdoptionPending = _useDynamicTarget; // nur sinnvoll, wenn die Drohne dem Punkt auch folgt
                   
                    _state[idx] = SampleState.Selected;
                    _testedEpoch[idx] = _epoch; // als getestet markieren
                    foundOK = true;
                    EndWave(clearTrails: false); // Punkt gefunden -> Welle beenden
                    break;
                }

                if (fired >= budgetThisTick) break; // Tick-Limit einhalten
            }

            if (!foundOK)
            {
                _state[idx] = SampleState.TestedBlocked;
                _testedEpoch[idx] = _epoch; // in dieser Epoche abgearbeitet
            }
        }
    }

    public void ResetHemiAdoptionFlag()
    {
        _hemiHasAdoptedOnce = false;
        _hemiAdoptionPending = false;
    }
    // unter deine anderen privaten Methoden
    private void HandleHemiFirstAdoption()
    {
        suspicionPositionReached = true;
        StopHemisphereScanner();
        DroneSetDestinationToReturnPosition();
    }

    // Markiert Welle als beendet. Trails optional behalten (für visuelles Nachglühen).
    private void EndWave(bool clearTrails)
    {
        _sinceLastSelection = 0f;
        _waveActive = false;
        _waveCursor = 0;
        if (clearTrails) _trails.Clear();
    }

    // Nächster Index aus der Wellen-Reihenfolge, der in dieser Epoche noch nicht getestet wurde
    private int NextIndexToTestThisWave()
    {
        int n = _order.Length;
        for (int k = 0; k < n; k++)
        {
            int i = _order[(_waveCursor + k) % n];

            // Schon in dieser Epoche getestet? Dann überspringen
            if (_testedEpoch != null && i < _testedEpoch.Length && _testedEpoch[i] == _epoch)
                continue;

            // Diesen als „queued“ markieren (nur visuell)
            _state[i] = SampleState.Queued;

            _waveCursor = (_waveCursor + k + 1) % n;
            return i;
        }
        return -1;
    }

    // NonAlloc-Puffer für statische Kapsel + Bestätigungs-Ray
    private readonly Collider[] _losCapsuleBuf = new Collider[64];
    private readonly RaycastHit[] _losRayBuf = new RaycastHit[32];

    // Statische Kapsel (Target<->Origin) + Bestätigungs-Ray.
    // Erfolg NUR, wenn in der Kapsel kein Fremd-Collider liegt
    // UND der erste gültige Ray-Hit das geforderte Tag trägt.
    private bool HasLineOfSightCapsule(Vector3 origin, Vector3 target, float radius, string requiredTag)
    {

        Vector3 d = target - origin;
        float dist = d.magnitude;
        if (dist < 1e-4f) {  DebugStoreLoSCapsule(origin, target, radius, false); return false; }
        Vector3 dir = d / dist;
        //Debug.Log("[HLoS] FALSE"); // muss vor DEBUGSTORELOSCAPSULE zwei zeilen weiter oben um zu funktionieren


        // 1) Statische Kapsel zwischen TARGET und ORIGIN (keine Bewegung)
        int n = Physics.OverlapCapsuleNonAlloc(
            point0: target,
            point1: origin,
            radius: radius,
            results: _losCapsuleBuf,
            layerMask: hemiSphereRaycastMask,
            queryTriggerInteraction: QueryTriggerInteraction.Ignore // Dummy kann Trigger sein
        );

        for (int i = 0; i < n; i++)
        {
            var col = _losCapsuleBuf[i];
            if (col == null) continue;

            // Eigenen Collider ignorieren
            if (_selfCollider != null && col == _selfCollider) continue;

            // Ziel-Tag explizit erlauben
            if (!string.IsNullOrEmpty(requiredTag) && col.CompareTag(requiredTag)) continue;

            // Irgendein anderer Collider in der Kapsel ⇒ blockiert
            //Debug.Log("[HLoS] FALSE");
            DebugStoreLoSCapsule(origin, target, radius, false);

            return false;
        }

        // 2) Bestätigungs-Ray: erster gültiger Hit MUSS requiredTag tragen
        int rc = Physics.RaycastNonAlloc(
            origin, dir, _losRayBuf, dist,
            hemiSphereRaycastMask,
            QueryTriggerInteraction.Collide
        );

        Collider first = null;
        float best = float.PositiveInfinity;

        for (int i = 0; i < rc; i++)
        {
            var h = _losRayBuf[i];
            var c = h.collider;
            if (c == null) continue;

            // Self ignorieren
            if (_selfCollider != null && c == _selfCollider) continue;

            if (h.distance < best) { best = h.distance; first = c; }
        }

        if (first == null) {  DebugStoreLoSCapsule(origin, target, radius, false); return false; }
        //Debug.Log("[HLoS] FALSE"); // muss vor DEBUGSTORELOSCAPSULE eine zeile weiter oben um zu funktionieren

        if (string.IsNullOrEmpty(requiredTag)) { DebugStoreLoSCapsule(origin, target, radius, true); return true; }
        //Debug.Log("[HLoS] TRUE"); // muss vor DEBUGSTORELOSCAPSULE eine zeile weiter oben um zu funktionieren


        bool ok = first.CompareTag(requiredTag);
        //Debug.Log(ok ? "[HLoS] TRUE" : "[HLoS] FALSE");
        DebugStoreLoSCapsule(origin, target, radius, ok);
        return ok;
    }


    /// Prüft: Liegt das Ziel im Sichtkegel des Searchlights (Halbwinkel in Grad)
    /// UND besteht freie Sicht (dicker Capsule- oder dünner Ray-Strahl)?
    /// - targetRoot: ein Transform in der Zielhierarchie (Root, Kopf, etc.)
    /// - halfAngleDeg: Sichtkegel-Halbwinkel in Grad (z. B. 30)
    /// - useThick: true = CapsuleCast, false = Raycast
    /// - capsuleRadius: Dicke des LoS-Casts (nur bei useThick relevant)
    /// - backstep: kürzt den Cast vor Ziel um Kollision „im Ziel“ zu vermeiden
    /// - mask: Layer, die Sicht blockieren dürfen
    /// - aimOffsetWorld: optionaler Welt-Offset vom targetRoot (z. B. (0,1.6,0))
    //public bool HasSearchlightSightToInCone(
    //    Transform targetRoot,
    //    float halfAngleDeg,
    //    bool useThick,
    //    float capsuleRadius,
    //    float backstep,
    //    LayerMask mask,
    //    Vector3 aimOffsetWorld = default)
    //{
    //    if (searchLight == null || targetRoot == null) return false;
    //
    //    // 1) Zielpunkt bestimmen
    //    Vector3 origin = searchLight.position;
    //    Vector3 target = targetRoot.position + aimOffsetWorld;
    //    Vector3 toTarget = target - origin;
    //    float dist = toTarget.magnitude;
    //    if (dist < 1e-4f) return false;
    //    Vector3 dir = toTarget / dist;
    //
    //    // 2) Sichtkegel-Prüfung (Winkel um die Forward-Achse des Searchlights)
    //    float angle = Vector3.Angle(searchLight.forward, dir);
    //    if (angle > Mathf.Max(0.0f, halfAngleDeg)) return false;
    //
    //    // 3) Line-of-Sight prüfen (dicker Capsule oder dünner Ray)
    //    //    Helper: gehört Collider zur Zielhierarchie?
    //    static bool ColliderBelongsTo(Transform root, Collider col)
    //    {
    //        if (col == null) return false;
    //        Transform t = col.transform;
    //        while (t != null) { if (t == root) return true; t = t.parent; }
    //        return false;
    //    }
    //
    //    // Eigenen Collider ignorieren, falls vorhanden
    //    Collider self = _selfCollider;
    //
    //    if (useThick)
    //    {
    //        float back = Mathf.Clamp(backstep, 0f, dist * 0.5f);
    //        Vector3 p0 = origin;
    //        Vector3 p1 = target - dir * back;
    //        float castDist = Mathf.Max(0f, dist - back);
    //
    //        // NonAlloc-Variante: _losHits[] als Klassenfeld (z. B. Größe 16)
    //        int hitCount = Physics.CapsuleCastNonAlloc(
    //            p0, p1, capsuleRadius, dir, _losHits, castDist, mask, QueryTriggerInteraction.Collide);
    //
    //        if (hitCount <= 0) return true; // nichts getroffen → Sicht frei
    //
    //        // Nächstgelegenen sinnvollen Hit (nicht self) ermitteln
    //        float best = float.PositiveInfinity;
    //        Collider bestCol = null;
    //        for (int i = 0; i < hitCount; i++)
    //        {
    //            var h = _losHits[i];
    //            if (h.collider == null) continue;
    //            if (self != null && h.collider == self) continue;
    //            if (h.distance < best) { best = h.distance; bestCol = h.collider; }
    //        }
    //
    //        if (bestCol == null) return true; // nur self o.ä. getroffen
    //        return ColliderBelongsTo(targetRoot, bestCol); // erster sinnvoller Hit gehört zum Ziel ⇒ Sicht ok
    //    }
    //    else
    //    {
    //        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Collide))
    //        {
    //            if (self != null && hit.collider == self)
    //            {
    //                // Falls der erste Hit „self“ ist, kurzes Re-Ray knapp dahinter
    //                const float eps = 0.02f;
    //                Vector3 o2 = hit.point + dir * eps;
    //                float d2 = Mathf.Max(0f, dist - hit.distance - eps);
    //                if (Physics.Raycast(o2, dir, out RaycastHit hit2, d2, mask, QueryTriggerInteraction.Collide))
    //                    return ColliderBelongsTo(targetRoot, hit2.collider);
    //                return true; // dahinter nichts → frei
    //            }
    //            return ColliderBelongsTo(targetRoot, hit.collider);
    //        }
    //        return true; // nichts getroffen → frei
    //    }
    //}

    public SightAndChannelResult CheckSightAndFireChannel(
    Transform targetRoot,
    float halfAngleDeg,
    LayerMask mask,
    // Sicht-Parameter:
    bool useCapsuleForLoS,      // true: LoS per Capsule, false: Ray
    float losCapsuleRadius,     // nur wenn useCapsuleForLoS = true
    float losBackstep,          // Verkürzung vor Ziel (Capsule)
    // Fire-Channel (immer CapsuleCast, i. d. R. dicker als LoS):
    float fireCapsuleRadius,
    float fireBackstep,
    // optionaler Zielpunktversatz (bei dir: Vector3.zero)
    Vector3 aimOffsetWorld)
    {
        var res = new SightAndChannelResult { inCone = false, hasLoS = false, hasFireChannel = false };
        if (searchLight == null || targetRoot == null) return res;

        // Zielrichtung & Distanz
        Vector3 origin = searchLight.position;
        Vector3 target = targetRoot.position + aimOffsetWorld;
        Vector3 toTarget = target - origin;
        float dist = toTarget.magnitude;
        if (dist < 1e-4f) return res;

        Vector3 dir = toTarget / dist;

        // 1) Sichtkegel
        if (Vector3.Angle(searchLight.forward, dir) > Mathf.Max(0f, halfAngleDeg))
            return res; // außerhalb des Kegels -> direkt „false, false“
        res.inCone = true;

        // Helper
        static bool BelongsTo(Transform root, Collider col)
        {
            if (col == null) return false;
            Transform t = col.transform;
            while (t != null) { if (t == root) return true; t = t.parent; }
            return false;
        }
        Transform selfRoot = transform.root;

        // 2) „Normale“ Sicht (LoS)
        bool losOK;
        if (useCapsuleForLoS)
        {
            float originEps = Mathf.Max(0.01f, losCapsuleRadius * 0.5f);
            float back = Mathf.Clamp(losBackstep, 0f, Mathf.Max(0f, dist - originEps) * 0.5f);
            Vector3 p0 = origin + dir * originEps;
            Vector3 p1 = target - dir * back;
            float castDist = Mathf.Max(0f, dist - back - originEps);

            int n = Physics.CapsuleCastNonAlloc(p0, p1, losCapsuleRadius * 0.5f, dir, _losHits, castDist, mask, QueryTriggerInteraction.Ignore);
            if (n <= 0) losOK = true;
            else
            {
                Collider best = null; float bestD = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    var h = _losHits[i];
                    if (h.collider == null) continue;
                    if (selfRoot != null && h.collider.transform.IsChildOf(selfRoot)) continue;
                    if (h.distance < bestD) { bestD = h.distance; best = h.collider; }
                }
                losOK = (best == null) || BelongsTo(targetRoot, best);
            }
        }
        else
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
            {
                if (selfRoot != null && hit.collider.transform.IsChildOf(selfRoot))
                {
                    float eps = 0.02f;
                    Vector3 o2 = hit.point + dir * eps;
                    float d2 = Mathf.Max(0f, dist - hit.distance - eps);
                    if (Physics.Raycast(o2, dir, out RaycastHit hit2, d2, mask, QueryTriggerInteraction.Ignore))
                        losOK = BelongsTo(targetRoot, hit2.collider);
                    else losOK = true;
                }
                else losOK = BelongsTo(targetRoot, hit.collider);
            }
            else losOK = true;
        }

        res.hasLoS = losOK;
        if (!losOK) return res; // kein Kanaltest nötig

        // 3) Fire-Channel (immer Capsule, meist dicker Radius)
        {
            float originEps = Mathf.Max(0.01f, fireCapsuleRadius * 0.5f);
            float back = Mathf.Clamp(fireBackstep, 0f, Mathf.Max(0f, dist - originEps) * 0.5f);
            Vector3 p0 = origin + dir * originEps;
            Vector3 p1 = target - dir * back;
            float castDist = Mathf.Max(0f, dist - back - originEps);

            int n = Physics.CapsuleCastNonAlloc(p0, p1, fireCapsuleRadius * 0.5f, dir, _losHits, castDist, mask, QueryTriggerInteraction.Ignore);
            if (n <= 0) res.hasFireChannel = true;
            else
            {
                Collider best = null; float bestD = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    var h = _losHits[i];
                    if (h.collider == null) continue;
                    if (selfRoot != null && h.collider.transform.IsChildOf(selfRoot)) continue;
                    if (h.distance < bestD) { bestD = h.distance; best = h.collider; }
                }
                res.hasFireChannel = (best == null) || BelongsTo(targetRoot, best);
            }
        }

        return res;
    }

    #endregion

    #region ColourChangeMethods
    public void SetEyeMeshLightAndParticleColors(int paletteIndex)
    {
        const float fade = 0.1f;
        lightPaletteScript.SetColor(paletteIndex, fade);
        meshMaterialPaletteScript.SetColor(paletteIndex, fade);
        particleMaterialPaletteScript.SetColor(paletteIndex, fade);
    }

    #endregion

    public void PeriodicCallout()
    {
        Vector3 targetPos = target.transform.position;

        // Run this check every frame, but only act if timer has elapsed
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            //Debug.Log("X");
            SoundManager.PlaySound(targetPos, 100f, 90f);
            timer = interval; // reset timer
        }
    }
    #region AttackMethods
    private void DoesHeSeeAndCanHeFire()
    {
            var sight = CheckSightAndFireChannel(
        targetRoot: player,
        halfAngleDeg: 30f,
        mask: hemiSphereRaycastMask,

        // normale Sicht: Ray oder schlanke Capsule
        useCapsuleForLoS: false,      // dünn = Ray; setze true, wenn du auch „dick“ willst
        losCapsuleRadius: 0.05f,      // nur relevant, wenn true
        losBackstep: 0f,

        // Fire-Channel: dicke Capsule (z. B. Projektil/Beam-Breite)
        fireCapsuleRadius: 0.20f,
        fireBackstep: 0.12f,

        // Zieloffset (bei dir aktuell 0)
        aimOffsetWorld: Vector3.zero
    );
        // Auswertung:
        seesPlayer = sight.inCone && sight.hasLoS;
        canFire = sight.inCone && sight.hasFireChannel;
        //Debug.Log("CanSeePlayer" + seesPlayer);
        //Debug.Log("CanFire" + canFire);
    }

    /// <summary>
    /// Startet das Partikelsystem, wenn canFire true wird,
    /// und deaktiviert es wieder, sobald canFire false ist.
    /// Redundant-Aufrufe werden vermieden.
    /// </summary>
    private void UpdateFireFX()
    {
        if (fireFX == null || droneFireFXSound == null) return;

        if (canFire)
        {
            var em = fireFX.emission;
            em.enabled = true;
            if (!fireFX.isPlaying) fireFX.Play(true);

            // Sound nur einmal beim Übergang einschalten
            if (!_fxSoundActive)
            {
                if (!droneFireFXSound.isPlaying) droneFireFXSound.Play();
                _fxSoundActive = true;   // Latch am Ende der Play-Logik
            }

            _fxActive = true;            // rein informativ
        }
        else
        {
            var em = fireFX.emission;
            em.enabled = false;
            if (fireFX.isPlaying) fireFX.Stop(true, stopBehavior);

            // Sound nur beim Übergang ausschalten 
            if (_fxSoundActive)
            {
                if (droneFireFXSound.isPlaying) droneFireFXSound.Stop();
                _fxSoundActive = false;  // Latch am Ende der Stop-Logik
            }

            _fxActive = false;
        }
    }


    #endregion

    #region ScannerMethods
    private void CheckScannerToggleOnce()
    {
        if (scanner == null) return;

        // Für GameObject.SetActive(...) ist activeSelf passend.
        // Wenn du Hierarchie-Abhängigkeiten berücksichtigen willst, nutze activeInHierarchy.
        bool isActiveNow = scanner.activeSelf;

        // Erste Initialisierung: kein „falscher“ Trigger beim ersten Aufruf
        if (!_scannerBaselineSet)
        {
            _scannerWasActive = isActiveNow;
            _scannerBaselineSet = true;
            return;
        }

        // Umschalt-Fälle:
        if (!_scannerWasActive && isActiveNow)
        {
            droneLightSoundActivate.Play();
        }
        else if (_scannerWasActive && !isActiveNow)
        {
            droneLightSoundDeactivate.Play();
        }

        // Latch am Ende aktualisieren
        _scannerWasActive = isActiveNow;
    }
    #endregion

    #region SightCheck
    private bool HasSightTo(Transform t)
    {
        if (t == null || searchLight == null) return false;

        // gleicher Kegel wie vorher (30°):
        Vector3 dir = t.position - searchLight.position;
        if (dir.sqrMagnitude < 1e-6f) return false;

        // gleiche dicke LoS wie der Scanner:
        bool thickLoS = HasLineOfSightCapsule(
            searchLight.position,
            t.position,
            losCapsuleRadius,   // selbes Serialized Feld wie oben/Scanner
            sightTag            // "Player" oder "SuspicionDummy" – du setzt das bereits passend
        );

        return thickLoS;
    }



    #endregion

    #region LOSMethods
    private static void DrawWireCapsuleGizmo(Vector3 a, Vector3 b, float r, Color col)
    {
        Gizmos.color = col;

        // Endkappen
        Gizmos.DrawWireSphere(a, r);
        Gizmos.DrawWireSphere(b, r);

        // Zylinder-Seiten als Liniennetz
        Vector3 axis = b - a;
        float len = axis.magnitude;
        if (len < 1e-6f) return;
        Vector3 n = axis / len;

        // Kreis-Basisvektoren orthogonal zur Achse
        Vector3 t = Vector3.Cross(n, Vector3.up);
        if (t.sqrMagnitude < 1e-6f) t = Vector3.Cross(n, Vector3.right);
        t.Normalize();
        Vector3 u = Vector3.Cross(n, t); // auch orthogonal, normiert

        const int SEG = 24;
        Vector3 prevA = Vector3.zero, prevB = Vector3.zero;
        for (int i = 0; i <= SEG; i++)
        {
            float ang = (i / (float)SEG) * Mathf.PI * 2f;
            Vector3 ring = Mathf.Cos(ang) * t + Mathf.Sin(ang) * u;
            Vector3 pA = a + ring * r;
            Vector3 pB = b + ring * r;

            // Längslinien
            Gizmos.DrawLine(pA, pB);

            // Ringe an den Enden
            if (i > 0)
            {
                Gizmos.DrawLine(prevA, pA);
                Gizmos.DrawLine(prevB, pB);
            }
            prevA = pA; prevB = pB;
        }
    }

    #endregion

    // Optional sauberes Aufräumen (z. B. beim Szenenwechsel / Disable)
    private void OnDisable()
    {
        if (fireFX != null)
            fireFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
   }