using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class EnhancedMeshGenerator : MonoBehaviour
{
    [Header("Rendering")]
    public Material material;
    public Material finishMaterial;

    [Header("Player Settings")]
    public float moveSpeed = 6f;
    public float airControlMultiplier = 0.5f;
    public float jumpForce = 20.4f; 
    public float maxFallSpeed = -15f;

    [Header("UI - TextMeshPro")]
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI statusText;

    [Header("Camera")]
    public PlayerCameraFollow cameraFollow;

    private Mesh cubeMesh;

    // Player with Rigidbody
    private GameObject playerObject;
    private Rigidbody playerRigidbody;
    private BoxCollider playerCollider;
    private bool isGrounded = false;
    private bool isInvincible = false;
    private int groundedFrames = 0;

    private float constantZ = 0f;
    private float playerWidth = 0.9f;
    private float playerHeight = 0.9f;
    private float playerDepth = 0.9f;

    private int playerLives = 3;
    private float timer = 0f;
    private bool gameOver = false;
    private bool levelComplete = false;

    // Anti-stuck mechanism
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private const float stuckThreshold = 0.05f;
    private const float stuckTimeLimit = 0.3f;

    // Anti-freeze detection
    private float freezeCheckTimer = 0f;
    private Vector3 lastVelocity;

    private class Fireball
    {
        public GameObject obj;
        public Rigidbody rb;
        public float lifetime = 3f;
    }

    private class Powerup
    {
        public GameObject obj;
        public bool active = true;
        public int type = 0;
    }

    private class Enemy
    {
        public GameObject obj;
        public Vector3 startPos;
        public float speed = 2f;
        public float distance = 3f;
        public int direction = 1;
        public bool active = true;
    }

    private List<Powerup> powerups = new List<Powerup>();
    private List<Fireball> fireballs = new List<Fireball>();
    private List<Enemy> enemies = new List<Enemy>();

    void Start()
    {
        SetupCamera();
        CreateCubeMesh();
        CreatePlayerWithPhysics();
        GenerateWorld();
        GeneratePowerups();
        GenerateEnemies();
        UpdateUI();

        lastPosition = Vector3.zero;
        lastVelocity = Vector3.zero;
    }

    void SetupCamera()
    {
        if (cameraFollow == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraFollow = mainCamera.GetComponent<PlayerCameraFollow>();
                if (cameraFollow == null)
                {
                    cameraFollow = mainCamera.gameObject.AddComponent<PlayerCameraFollow>();
                }
            }
            else
            {
                GameObject cameraObj = new GameObject("PlayerCamera");
                Camera cam = cameraObj.AddComponent<Camera>();
                cameraFollow = cameraObj.AddComponent<PlayerCameraFollow>();
                cam.tag = "MainCamera";
            }

            cameraFollow.offset = new Vector3(0, 5, -15);
            cameraFollow.smoothSpeed = 8f;
            cameraFollow.lookAtPlayer = true;
            cameraFollow.followX = true;
            cameraFollow.followY = true;
            cameraFollow.followZ = false;
        }
    }

    void Update()
    {
        if (gameOver || levelComplete)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                );
            }
            return;
        }

        timer += Time.deltaTime;
        UpdateTimerUI();

        HandleInput();
        CheckGrounded();
        CheckIfStuck();
        CheckForFreeze();
        UpdateEnemies();
        UpdateFireballs();
        UpdateCamera();

        // Death if fell off
        if (playerObject != null && playerObject.transform.position.y < -10)
        {
            KillPlayer();
        }
    }

    void FixedUpdate()
    {
        if (gameOver || levelComplete) return;

        if (playerRigidbody != null)
        {
            ApplyMovement();
            LimitFallSpeed();
            PreventPhysicsFreeze();
            StabilizeOnGround();
        }
    }

    void LimitFallSpeed()
    {
        if (playerRigidbody.linearVelocity.y < maxFallSpeed)
        {
            Vector3 vel = playerRigidbody.linearVelocity;
            vel.y = maxFallSpeed;
            playerRigidbody.linearVelocity = vel;
        }
    }

    void StabilizeOnGround()
    {
        // CRITICAL FIX: When grounded, force Y velocity to zero to prevent hopping
        if (isGrounded && groundedFrames > 2)
        {
            Vector3 vel = playerRigidbody.linearVelocity;
            if (vel.y < 0.5f) // Only stabilize if not intentionally jumping
            {
                vel.y = 0f;
                playerRigidbody.linearVelocity = vel;
            }
        }
    }

    void PreventPhysicsFreeze()
    {
        // Ensure Rigidbody never sleeps
        if (playerRigidbody.IsSleeping())
        {
            playerRigidbody.WakeUp();
        }

        // Only apply downward force if truly airborne (not just 1 frame off ground)
        if (playerRigidbody.linearVelocity.sqrMagnitude < 0.001f && !isGrounded && groundedFrames > 5)
        {
            Vector3 vel = playerRigidbody.linearVelocity;
            vel.y -= 0.1f;
            playerRigidbody.linearVelocity = vel;
        }
    }

    void CheckForFreeze()
    {
        freezeCheckTimer += Time.deltaTime;

        // Check every 0.5 seconds
        if (freezeCheckTimer > 0.5f)
        {
            if (playerRigidbody != null)
            {
                Vector3 currentVel = playerRigidbody.linearVelocity;

                // Only force downward if clearly frozen in mid-air
                if (currentVel.sqrMagnitude < 0.001f && !isGrounded && groundedFrames > 10 && playerObject.transform.position.y > 0.5f)
                {
                    playerRigidbody.WakeUp();
                    currentVel.y = -1f;
                    playerRigidbody.linearVelocity = currentVel;
                }

                lastVelocity = currentVel;
            }

            freezeCheckTimer = 0f;
        }
    }

    void CheckIfStuck()
    {
        if (playerObject == null) return;

        Vector3 currentPos = playerObject.transform.position;
        float distance = Vector3.Distance(currentPos, lastPosition);

        float input = Input.GetAxisRaw("Horizontal");

        // Only trigger unstuck if clearly stuck (not just standing still)
        if (Mathf.Abs(input) > 0.1f && distance < stuckThreshold && groundedFrames > 5)
        {
            stuckTimer += Time.deltaTime;

            if (stuckTimer > stuckTimeLimit)
            {
                Vector3 vel = playerRigidbody.linearVelocity;

                // Upward boost
                vel.y = 4f;

                // Horizontal push
                vel.x = input * 2f;

                playerRigidbody.linearVelocity = vel;
                playerRigidbody.WakeUp();

                stuckTimer = 0f;

                if (statusText) statusText.text = "Unstuck!";
                Invoke(nameof(ClearStatusText), 1f);
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPosition = currentPos;
    }

    void UpdateCamera()
    {
        if (cameraFollow != null && playerObject != null)
        {
            cameraFollow.SetPlayerPosition(playerObject.transform.position);
        }
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            ShootFireball();
        }
    }

    void CreatePlayerWithPhysics()
    {
        playerObject = new GameObject("Player");
        playerObject.transform.position = new Vector3(0, 2, constantZ);

        // Physics material with minimal friction
        PhysicsMaterial playerMat = new PhysicsMaterial("PlayerPhysicsMaterial");
        playerMat.dynamicFriction = 0.1f; // Slight friction to prevent infinite sliding
        playerMat.staticFriction = 0.1f;
        playerMat.bounciness = 0f; // NO bouncing
        playerMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        playerMat.bounceCombine = PhysicsMaterialCombine.Minimum;

        // Rigidbody setup
        playerRigidbody = playerObject.AddComponent<Rigidbody>();
        playerRigidbody.mass = 1f;
        playerRigidbody.linearDamping = 1f; // Increased damping to prevent bouncing
        playerRigidbody.angularDamping = 0.05f;
        playerRigidbody.useGravity = true;
        playerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        playerRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // CRITICAL: Prevent sleeping
        playerRigidbody.sleepThreshold = 0f;

        // Freeze all rotations and Z position
        playerRigidbody.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationY |
            RigidbodyConstraints.FreezeRotationZ |
            RigidbodyConstraints.FreezePositionZ;

        // BoxCollider with physics material
        playerCollider = playerObject.AddComponent<BoxCollider>();
        playerCollider.size = new Vector3(playerWidth, playerHeight, playerDepth);
        playerCollider.center = new Vector3(0, 0, 0);
        playerCollider.material = playerMat;

        // Visual mesh
        MeshFilter mf = playerObject.AddComponent<MeshFilter>();
        mf.mesh = cubeMesh;
        MeshRenderer mr = playerObject.AddComponent<MeshRenderer>();
        mr.material = material;

        // Collision handler
        PlayerCollisionHandler handler = playerObject.AddComponent<PlayerCollisionHandler>();
        handler.Initialize(this);
    }

    void ApplyMovement()
    {
        float horizontal = 0;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1;

        float effectiveSpeed = isGrounded ? moveSpeed : moveSpeed * airControlMultiplier;

        // Direct velocity control
        Vector3 velocity = playerRigidbody.linearVelocity;
        velocity.x = horizontal * effectiveSpeed;

        // CRITICAL: Preserve Y velocity unless grounded
        // Don't reset Y velocity every frame

        playerRigidbody.linearVelocity = velocity;

        // Wake up when moving
        if (Mathf.Abs(horizontal) > 0.01f)
        {
            playerRigidbody.WakeUp();
        }
    }

    void CheckGrounded()
    {
        if (playerCollider == null || playerObject == null) return;

        Vector3 origin = playerObject.transform.position;
        float rayLength = (playerHeight * 0.5f) + 0.1f; // Reduced from 0.2f

        // Single center raycast is more stable than multiple
        RaycastHit hit;
        bool wasGrounded = isGrounded;
        isGrounded = Physics.Raycast(origin, Vector3.down, out hit, rayLength);

        // Track how many frames we've been grounded
        if (isGrounded)
        {
            groundedFrames++;
        }
        else
        {
            groundedFrames = 0;
        }

        // Jump - only if firmly grounded
        if (isGrounded && groundedFrames > 2 && Input.GetKeyDown(KeyCode.Space))
        {
            Vector3 vel = playerRigidbody.linearVelocity;
            vel.y = jumpForce;
            playerRigidbody.linearVelocity = vel;
            playerRigidbody.WakeUp();
            isGrounded = false;
            groundedFrames = 0;
        }
    }

    public void OnPlayerTriggerEnter(Collider other)
    {
        string objName = other.gameObject.name;

        if (objName.StartsWith("Powerup"))
        {
            CollectPowerup(other.gameObject);
        }
        else if (objName.StartsWith("Enemy") && !isInvincible)
        {
            HitPlayer();
        }
        else if (objName.StartsWith("Spike") && !isInvincible)
        {
            KillPlayer();
        }
        else if (objName == "FinishLine")
        {
            LevelComplete();
        }
    }

    void CollectPowerup(GameObject powerupObj)
    {
        foreach (var p in powerups)
        {
            if (p.obj == powerupObj && p.active)
            {
                p.active = false;

                switch (p.type)
                {
                    case 0:
                        isInvincible = true;
                        CancelInvoke(nameof(RemoveInvincibility));
                        Invoke(nameof(RemoveInvincibility), 3f);
                        if (statusText) statusText.text = "Invincible!";
                        break;
                    case 1:
                        ShootFireball();
                        if (statusText) statusText.text = "Fireball!";
                        break;
                    case 2:
                        playerLives++;
                        UpdateUI();
                        if (statusText) statusText.text = "Extra Life!";
                        break;
                }

                Destroy(powerupObj);
                Invoke(nameof(ClearStatusText), 1f);
                break;
            }
        }
    }

    void ClearStatusText()
    {
        if (statusText) statusText.text = "";
    }

    void KillPlayer()
    {
        playerLives = 0;
        gameOver = true;
        UpdateUI();
    }

    void HitPlayer()
    {
        if (isInvincible) return;

        playerLives--;
        UpdateUI();

        if (playerLives <= 0)
        {
            KillPlayer();
        }
        else
        {
            isInvincible = true;
            CancelInvoke(nameof(RemoveInvincibility));
            Invoke(nameof(RemoveInvincibility), 1f);
            if (statusText) statusText.text = "Hit!";
            Invoke(nameof(ClearStatusText), 1f);
        }
    }

    void RemoveInvincibility()
    {
        isInvincible = false;
    }

    void LevelComplete()
    {
        levelComplete = true;
        if (hpText) hpText.text = "LEVEL COMPLETE!\nTime: " + timer.ToString("F2") + "s\nPress R";
    }

    void GenerateEnemies()
    {
        SpawnEnemy(new Vector3(20, 1, constantZ));
        SpawnEnemy(new Vector3(40, 1, constantZ));
        SpawnEnemy(new Vector3(68, 1, constantZ));
        SpawnEnemy(new Vector3(92, 1, constantZ));
        SpawnEnemy(new Vector3(122, 1, constantZ));
        SpawnEnemy(new Vector3(155, 1, constantZ));
    }

    void SpawnEnemy(Vector3 start)
    {
        GameObject enemyObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        enemyObj.transform.position = start;
        enemyObj.transform.localScale = Vector3.one;
        enemyObj.name = "Enemy";

        BoxCollider col = enemyObj.GetComponent<BoxCollider>();
        col.isTrigger = true;

        MeshRenderer mr = enemyObj.GetComponent<MeshRenderer>();
        if (material) mr.material = material;

        Enemy enemy = new Enemy
        {
            obj = enemyObj,
            startPos = start,
            active = true
        };
        enemies.Add(enemy);
    }

    void UpdateEnemies()
    {
        foreach (var e in enemies)
        {
            if (!e.active || e.obj == null) continue;

            Vector3 pos = e.obj.transform.position;
            pos.x += e.direction * e.speed * Time.deltaTime;

            if (Mathf.Abs(pos.x - e.startPos.x) > e.distance)
                e.direction *= -1;

            e.obj.transform.position = pos;
        }
    }

    void GeneratePowerups()
    {
        SpawnPowerup(new Vector3(15, 5, constantZ), 0);  // Invincibility
        SpawnPowerup(new Vector3(35, 9, constantZ), 1);  // Fireball
        SpawnPowerup(new Vector3(60, 11, constantZ), 2); // Extra life
        SpawnPowerup(new Vector3(85, 13, constantZ), 0); // Invincibility
        SpawnPowerup(new Vector3(115, 10, constantZ), 1); // Fireball
        SpawnPowerup(new Vector3(145, 11, constantZ), 2); // Extra life
        SpawnPowerup(new Vector3(170, 17, constantZ), 0); // Invincibility
    }

    void SpawnPowerup(Vector3 pos, int type)
    {
        GameObject powerupObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        powerupObj.transform.position = pos;
        powerupObj.transform.localScale = Vector3.one * 0.6f;
        powerupObj.name = "Powerup_" + type;

        BoxCollider col = powerupObj.GetComponent<BoxCollider>();
        col.isTrigger = true;

        MeshRenderer mr = powerupObj.GetComponent<MeshRenderer>();
        if (material) mr.material = material;

        Powerup powerup = new Powerup
        {
            obj = powerupObj,
            active = true,
            type = type
        };
        powerups.Add(powerup);
    }

    void ShootFireball()
    {
        if (playerObject == null) return;

        Vector3 start = playerObject.transform.position + Vector3.right * 1.5f;
        GameObject fireballObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fireballObj.transform.position = start;
        fireballObj.transform.localScale = Vector3.one * 0.5f;
        fireballObj.name = "Fireball";

        Rigidbody rb = fireballObj.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearVelocity = Vector3.right * 12f;
        rb.sleepThreshold = 0f;

        SphereCollider col = fireballObj.GetComponent<SphereCollider>();
        col.isTrigger = true;

        MeshRenderer mr = fireballObj.GetComponent<MeshRenderer>();
        if (material) mr.material = material;

        Fireball fireball = new Fireball
        {
            obj = fireballObj,
            rb = rb,
            lifetime = 3f
        };
        fireballs.Add(fireball);
    }

    void UpdateFireballs()
    {
        for (int i = fireballs.Count - 1; i >= 0; i--)
        {
            Fireball f = fireballs[i];
            if (f.obj == null)
            {
                fireballs.RemoveAt(i);
                continue;
            }

            f.lifetime -= Time.deltaTime;

            if (f.lifetime <= 0 || Mathf.Abs(f.obj.transform.position.x) > 60f)
            {
                Destroy(f.obj);
                fireballs.RemoveAt(i);
                continue;
            }

            Collider[] hits = Physics.OverlapSphere(f.obj.transform.position, 0.5f);
            foreach (var hit in hits)
            {
                if (hit.gameObject.name.StartsWith("Enemy"))
                {
                    foreach (var e in enemies)
                    {
                        if (e.obj == hit.gameObject && e.active)
                        {
                            e.active = false;
                            Destroy(e.obj);
                            Destroy(f.obj);
                            fireballs.RemoveAt(i);
                            goto NextFireball;
                        }
                    }
                }
            }

        NextFireball:;
        }
    }

    void GenerateWorld()
    {
        // Main ground platform - extended
        for (int i = -5; i < 80; i++)
        {
            Vector3 pos = new Vector3(i * 3f, -1, constantZ);
            CreateStaticPlatform(pos, new Vector3(3f, 1f, 1f), false);
        }

        // Early platforms - easy jumps
        CreateStaticPlatform(new Vector3(5f, 2f, constantZ), new Vector3(4f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(12f, 4f, constantZ), new Vector3(4f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(18f, 6f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(25f, 8f, constantZ), new Vector3(4f, 1f, 1f), false);

        // Mid section - varied heights
        CreateStaticPlatform(new Vector3(32f, 6f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(38f, 10f, constantZ), new Vector3(4f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(45f, 7f, constantZ), new Vector3(6f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(52f, 12f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(58f, 9f, constantZ), new Vector3(4f, 1f, 1f), false);

        // Challenge section - higher platforms
        CreateStaticPlatform(new Vector3(65f, 14f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(72f, 11f, constantZ), new Vector3(6f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(80f, 15f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(88f, 10f, constantZ), new Vector3(7f, 1f, 1f), false);

        // Stepping stones - small platforms
        CreateStaticPlatform(new Vector3(95f, 8f, constantZ), new Vector3(3f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(100f, 11f, constantZ), new Vector3(3f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(105f, 13f, constantZ), new Vector3(3f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(110f, 10f, constantZ), new Vector3(4f, 1f, 1f), false);

        // Bridge section - horizontal
        CreateStaticPlatform(new Vector3(118f, 8f, constantZ), new Vector3(8f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(130f, 12f, constantZ), new Vector3(6f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(140f, 10f, constantZ), new Vector3(7f, 1f, 1f), false);

        // Final ascent - going up
        CreateStaticPlatform(new Vector3(150f, 13f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(158f, 16f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(165f, 18f, constantZ), new Vector3(6f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(173f, 15f, constantZ), new Vector3(5f, 1f, 1f), false);
        CreateStaticPlatform(new Vector3(180f, 12f, constantZ), new Vector3(8f, 1f, 1f), false);

        // Obstacles - instakill spikes (placed strategically)
        CreateStaticPlatform(new Vector3(30f, 0.5f, constantZ), new Vector3(1f, 3f, 1f), true);
        CreateStaticPlatform(new Vector3(55f, 0.5f, constantZ), new Vector3(1f, 2f, 1f), true);
        CreateStaticPlatform(new Vector3(75f, 0.5f, constantZ), new Vector3(1f, 4f, 1f), true);
        CreateStaticPlatform(new Vector3(98f, 0.5f, constantZ), new Vector3(1f, 3f, 1f), true);
        CreateStaticPlatform(new Vector3(125f, 0.5f, constantZ), new Vector3(1f, 2f, 1f), true);
        CreateStaticPlatform(new Vector3(160f, 0.5f, constantZ), new Vector3(1f, 4f, 1f), true);

        // Finish line at the end
        CreateFinishLine(new Vector3(190f, 14f, constantZ));
    }

    void CreateFinishLine(Vector3 pos)
    {
        GameObject finishLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        finishLine.transform.position = pos;
        finishLine.transform.localScale = new Vector3(2f, 5f, 1f);
        finishLine.name = "FinishLine";

        BoxCollider col = finishLine.GetComponent<BoxCollider>();
        col.isTrigger = true;

        MeshRenderer mr = finishLine.GetComponent<MeshRenderer>();

        if (finishMaterial != null)
        {
            mr.material = finishMaterial;
        }
        else
        {
            Material finishMat = new Material(Shader.Find("Standard"));
            finishMat.color = Color.yellow;
            mr.material = finishMat;
        }
    }

    void CreateStaticPlatform(Vector3 pos, Vector3 scale, bool isInstakill)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.transform.position = pos;
        platform.transform.localScale = scale;
        platform.isStatic = true;
        platform.name = isInstakill ? "Spike" : "Platform";

        BoxCollider col = platform.GetComponent<BoxCollider>();

        if (isInstakill)
        {
            col.isTrigger = true;
        }
        else
        {
            // Platform physics material - minimal friction, NO bounce
            PhysicsMaterial platformMat = new PhysicsMaterial("PlatformMaterial");
            platformMat.dynamicFriction = 0.2f;
            platformMat.staticFriction = 0.2f;
            platformMat.bounciness = 0f; // CRITICAL: No bouncing!
            platformMat.frictionCombine = PhysicsMaterialCombine.Minimum;
            platformMat.bounceCombine = PhysicsMaterialCombine.Minimum;
            col.material = platformMat;
        }

        MeshRenderer mr = platform.GetComponent<MeshRenderer>();
        if (material) mr.material = material;
    }

    void CreateCubeMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp);
    }

    void UpdateUI()
    {
        if (hpText)
        {
            if (gameOver)
                hpText.text = "GAME OVER!\nPress R to Restart";
            else
                hpText.text = "HP: " + playerLives + "/" + 3;
        }
    }

    void UpdateTimerUI()
    {
        if (timerText) timerText.text = "Time: " + timer.ToString("F2") + "s";
    }
}

public class PlayerCollisionHandler : MonoBehaviour
{
    private EnhancedMeshGenerator manager;

    public void Initialize(EnhancedMeshGenerator mgr)
    {
        manager = mgr;
    }

    void OnTriggerEnter(Collider other)
    {
        if (manager != null)
        {
            manager.OnPlayerTriggerEnter(other);
        }
    }
}