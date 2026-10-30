using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    [InitializeOnLoad]
    public static class WorldCombatValidation
    {
        private const string RunningKey = "LootTheDeep.WorldCombatValidation.Running";
        private static int step;
        private static int checks;
        private static double nextCheck;
        private static double deadline;
        private static float nextSimulationCheck;
        private static PlayerShooter2D shooter;
        private static CombatHealth2D player;
        private static EnemyCombat2D guard;
        private static CombatHealth2D target;
        private static EnemyCombat2D raider;
        private static GameObject wall;
        private static int beforeMelee;
        private static Vector2 lockedDirection;

        static WorldCombatValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        // This regression intentionally checks the original demonstration encounter, not arbitrary layouts.
        [MenuItem("LootTheDeep/战斗/验证演示布局战斗（运行测试） %#F9")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
                scene.path != "Assets/Scenes/ExploreScene.unity")
                throw new InvalidOperationException("请先保存 ExploreScene，并在编辑模式下运行战斗检查。");
            if (Object.FindFirstObjectByType<CombatPrototypeSession2D>() == null ||
                Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None).Length != 2)
                throw new InvalidOperationException("请先接入首版战斗。");
            Debug.Log($"WORLD_COMBAT_VALIDATION_START: dataPath={Application.dataPath}; product={Application.productName}; scene={scene.path}");
            SessionState.SetBool(RunningKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunningKey, false))
            {
                step = checks = 0;
                nextCheck = EditorApplication.timeSinceStartup + .5;
                nextSimulationCheck = .1f;
                deadline = EditorApplication.timeSinceStartup + 40;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(RunningKey, false);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused ||
                EditorApplication.timeSinceStartup < nextCheck) return;
            try
            {
                CheckTime();
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.time < nextSimulationCheck) return;
                if (step == 0)
                {
                    shooter = Object.FindFirstObjectByType<PlayerShooter2D>();
                    player = shooter.GetComponent<CombatHealth2D>();
                    Check(player.CurrentHealth == 100, "角色生命值与枪械正确接入");
                    GameObject.Find("CombatEncounter").SetActive(false);
                    MovePlayer(new Vector2(0f, -200f));
                    guard = SpawnEnemy(WorldCombatInstaller.GuardPath, new Vector2(4f, -200f));
                    guard.enabled = false;
                    target = guard.GetComponent<CombatHealth2D>();
                    Shoot(new Vector2(2f, -200f), Vector2.right, shooter.GetComponent<Collider2D>());
                    Advance(.4);
                }
                else if (step == 1)
                {
                    Check(target.CurrentHealth == 90, $"玩家弹丸只结算一次伤害 (hp={target.CurrentHealth}, time={Time.time}, scale={Time.timeScale}, shots={Object.FindObjectsByType<ShootingProjectile2D>(FindObjectsSortMode.None).Length})");
                    Transform bar = guard.transform.Find("HealthBar");
                    Transform fill = bar.Find("Fill");
                    Check(Mathf.Approximately(fill.localScale.x, .75f) &&
                        fill.GetComponent<SpriteRenderer>().sortingOrder > bar.GetComponent<SpriteRenderer>().sortingOrder,
                        "敌人血条反映剩余生命，填充层不被底板遮挡");
                    var friendly = SpawnEnemy(WorldCombatInstaller.GuardPath, new Vector2(8f, -200f));
                    friendly.name = "FriendlyFireTarget";
                    friendly.enabled = false;
                    Shoot(new Vector2(6f, -200f), Vector2.right, guard.GetComponent<Collider2D>());
                    Advance(.4);
                }
                else if (step == 2)
                {
                    Check(GameObject.Find("FriendlyFireTarget").GetComponent<CombatHealth2D>().CurrentHealth == 120,
                        "敌方弹丸不伤害同阵营");
                    Object.Destroy(GameObject.Find("FriendlyFireTarget"));
                    ClearShots();
                    wall = new GameObject("ValidationWall");
                    wall.transform.position = new Vector2(2f, -200f);
                    wall.AddComponent<BoxCollider2D>().size = new Vector2(.4f, 4f);
                    Physics2D.SyncTransforms();
                    Shoot(new Vector2(1f, -200f), Vector2.right, shooter.GetComponent<Collider2D>());
                    Advance(.4);
                }
                else if (step == 3)
                {
                    Check(target.CurrentHealth == 90 && Object.FindObjectsByType<ShootingProjectile2D>(FindObjectsSortMode.None).Length == 0,
                        "墙体阻挡弹丸");
                    guard.enabled = true;
                    Advance(.9);
                }
                else if (step == 4)
                {
                    Check(!guard.IsWindingUp && player.CurrentHealth == 100, "墙体遮挡视线，敌人不会隔墙开火");
                    Object.Destroy(wall);
                    Advance(.15);
                }
                else if (step == 5)
                {
                    if (!guard.IsWindingUp) return;
                    lockedDirection = guard.AttackDirection;
                    Check(guard.transform.Find("AttackWarning").gameObject.activeSelf, "攻击前显示方向预警");
                    MovePlayer(new Vector2(0f, -197f));
                    Advance(.65);
                }
                else if (step == 6)
                {
                    Check(Vector2.Dot(guard.AttackDirection, lockedDirection) > .999f, "预警后攻击方向保持锁定");
                    Check(player.CurrentHealth == 100, "侧向走位可以躲开锁定攻击");
                    bool lockedShot = false;
                    foreach (var shot in Object.FindObjectsByType<ShootingProjectile2D>(FindObjectsSortMode.None))
                        if (Vector2.Dot(shot.GetComponent<Rigidbody2D>().linearVelocity.normalized, lockedDirection) > .999f)
                            lockedShot = true;
                    Check(lockedShot, "实际发射的弹丸沿预警方向前进");
                    ClearShots();
                    MovePlayer(new Vector2(0f, -200f));
                    Advance(3.3);
                }
                else if (step == 7)
                {
                    Check(player.CurrentHealth < 100, "枪手攻击能实际命中并扣除玩家生命值");
                    var healthFill = Object.FindFirstObjectByType<CombatPrototypeSession2D>().transform
                        .Find("HullStatus/HullTrack/HullFill").GetComponent<Image>();
                    Canvas.ForceUpdateCanvases();
                    var mesh = healthFill.canvasRenderer.GetMesh();
                    Check(healthFill.sprite != null && mesh != null && mesh.vertexCount > 0 &&
                        Mathf.Abs(mesh.bounds.size.x / healthFill.rectTransform.rect.width -
                            (float)player.CurrentHealth / player.MaxHealth) < .001f,
                        "玩家生命条实际绘制长度随剩余生命变化");
                    target.TakeDamage(target.MaxHealth);
                    Check(target.IsDead && !guard.IsWindingUp && !guard.GetComponent<Collider2D>().enabled &&
                        guard.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "敌人死亡后停止攻击、移动和碰撞");
                    ClearShots();
                    MovePlayer(new Vector2(0f, -200f));
                    beforeMelee = player.CurrentHealth;
                    raider = SpawnEnemy(WorldCombatInstaller.RaiderPath, new Vector2(2.6f, -200f));
                    Advance(1.2);
                }
                else if (step == 8)
                {
                    Check(player.CurrentHealth == beforeMelee - 18, "突进攻击接触时伤害一次，不连续逐帧扣血");
                    raider.GetComponent<CombatHealth2D>().TakeDamage(1000);
                    player.TakeDamage(1000);
                    var session = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
                    Check(session.IsFinished && Time.timeScale == 0f, "玩家死亡进入失败界面并暂停交战");
                    shooter.ActiveGun.TryFire(Vector2.right, Time.time + 100f, out _);
                    shooter.Reload();
                    Check(!shooter.ActiveGun.IsReloading, "玩家死亡后无法换弹");
                    session.Restart();
                    Advance(.8);
                    nextSimulationCheck = 0f;
                }
                else
                {
                    var currentPlayer = Object.FindFirstObjectByType<PlayerShooter2D>();
                    Check(Time.timeScale == 1f && currentPlayer.GetComponent<CombatHealth2D>().CurrentHealth == 100 &&
                        !Object.FindFirstObjectByType<CombatPrototypeSession2D>().IsFinished &&
                        Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None).Length == 2,
                        "重新开始恢复生命、时间和场景敌人");
                    Debug.Log($"WORLD_COMBAT_PLAY_CHECKS_PASSED: {checks} checks; projectile damage, teams, cover, telegraphs, dodge, ranged/melee, death and restart.");
                    SessionState.SetBool(RunningKey, false);
                    EditorApplication.update -= Tick;
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                SessionState.SetBool(RunningKey, false);
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
            }
        }

        private static void Advance(double seconds)
        {
            step++;
            nextCheck = EditorApplication.timeSinceStartup + .02;
            nextSimulationCheck = Time.time + (float)seconds;
            if (step == 9) nextCheck = EditorApplication.timeSinceStartup + seconds;
        }

        private static void CheckTime()
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception($"WORLD_COMBAT_CHECK_FAILED: timeout at step {step}");
        }

        private static EnemyCombat2D SpawnEnemy(string path, Vector2 position)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity);
            return root.GetComponent<EnemyCombat2D>();
        }

        private static void MovePlayer(Vector2 position)
        {
            shooter.GetComponent<Rigidbody2D>().position = position;
            shooter.transform.position = position;
            Physics2D.SyncTransforms();
        }

        private static void Shoot(Vector2 origin, Vector2 direction, Collider2D owner)
        {
            var shot = Object.Instantiate(shooter.ActiveGunConfig.projectilePrefab, origin, Quaternion.identity);
            shot.Fire(direction, 10f, 30, owner);
        }

        private static void ClearShots()
        {
            foreach (var shot in Object.FindObjectsByType<ShootingProjectile2D>(FindObjectsSortMode.None))
                Object.Destroy(shot.gameObject);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("WORLD_COMBAT_CHECK_FAILED: " + message);
            checks++;
        }
    }
}
