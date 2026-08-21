CREATE TABLE IF NOT EXISTS order_activities (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_id BIGINT UNSIGNED NOT NULL,
    actor_discord_user_id VARCHAR(32) NOT NULL,
    actor_display_name VARCHAR(190) NOT NULL,
    action_type VARCHAR(64) NOT NULL,
    detail TEXT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT fk_order_activities_order FOREIGN KEY (order_id) REFERENCES orders (id),
    INDEX ix_order_activities_order_created (order_id, created_at, id)
) ENGINE=InnoDB;
