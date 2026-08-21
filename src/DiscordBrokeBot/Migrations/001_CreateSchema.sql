CREATE TABLE IF NOT EXISTS orders (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    requester_discord_user_id VARCHAR(32) NOT NULL,
    requester_display_name VARCHAR(190) NOT NULL,
    buyer_discord_user_id VARCHAR(32) NOT NULL,
    buyer_display_name VARCHAR(190) NOT NULL,
    source_guild_id VARCHAR(32) NOT NULL,
    source_guild_name VARCHAR(190) NOT NULL,
    item_name VARCHAR(190) NOT NULL,
    unit_price BIGINT NOT NULL,
    quantity INT NOT NULL,
    note TEXT NULL,
    stall VARCHAR(190) NULL,
    is_purchased TINYINT(1) NOT NULL DEFAULT 0,
    purchased_at DATETIME(6) NULL,
    settlement_override VARCHAR(32) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    archived_at DATETIME(6) NULL,
    archived_by_discord_user_id VARCHAR(32) NULL,
    PRIMARY KEY (id),
    CONSTRAINT chk_orders_unit_price CHECK (unit_price > 0),
    CONSTRAINT chk_orders_quantity CHECK (quantity > 0),
    CONSTRAINT chk_orders_settlement_override CHECK (
        settlement_override IS NULL
        OR settlement_override IN ('force_completed', 'force_pending')
    ),
    INDEX ix_orders_buyer_active_created (buyer_discord_user_id, archived_at, created_at),
    INDEX ix_orders_requester_active_created (requester_discord_user_id, archived_at, created_at),
    INDEX ix_orders_source_guild (source_guild_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS payment_entries (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_id BIGINT UNSIGNED NOT NULL,
    amount BIGINT NOT NULL,
    reason VARCHAR(500) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT chk_payment_entries_amount CHECK (amount <> 0),
    CONSTRAINT fk_payment_entries_order FOREIGN KEY (order_id) REFERENCES orders (id),
    INDEX ix_payment_entries_order_created (order_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS user_blocks (
    buyer_discord_user_id VARCHAR(32) NOT NULL,
    requester_discord_user_id VARCHAR(32) NOT NULL,
    requester_display_name VARCHAR(190) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (buyer_discord_user_id, requester_discord_user_id),
    INDEX ix_user_blocks_requester (requester_discord_user_id)
) ENGINE=InnoDB;
